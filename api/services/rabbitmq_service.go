package services

import (
	"encoding/json"
	"fmt"
	"log"
	"sync"
	"time"

	amqp "github.com/rabbitmq/amqp091-go"
	"github.com/vecchiotom/reelforge/config"
)

// WorkflowEvent is the structure broadcast to SSE clients.
type WorkflowEvent struct {
	Type        string          `json:"type"`
	ExecutionID string          `json:"executionId"`
	Timestamp   time.Time       `json:"timestamp"`
	Data        json.RawMessage `json:"data"`
}

// SSEHub manages a set of SSE subscriber channels and broadcasts events to them.
type SSEHub struct {
	mu             sync.RWMutex
	clients        map[chan WorkflowEvent]string
	connectionsByUser map[string]int
}

var Hub = &SSEHub{
	clients:           make(map[chan WorkflowEvent]string),
	connectionsByUser: make(map[string]int),
}

const maxConnectionsPerUser = 3

// Subscribe returns a channel that will receive workflow events. Call Unsubscribe
// when the SSE connection closes.
func (h *SSEHub) Subscribe(scopeKey string) (chan WorkflowEvent, bool) {
	if scopeKey == "" {
		scopeKey = "anonymous"
	}

	ch := make(chan WorkflowEvent, 32)
	h.mu.Lock()
	if h.connectionsByUser[scopeKey] >= maxConnectionsPerUser {
		h.mu.Unlock()
		close(ch)
		return nil, false
	}
	h.clients[ch] = scopeKey
	h.connectionsByUser[scopeKey] = h.connectionsByUser[scopeKey] + 1
	h.mu.Unlock()
	return ch, true
}

// Unsubscribe removes the channel and closes it.
func (h *SSEHub) Unsubscribe(ch chan WorkflowEvent) {
	h.mu.Lock()
	scopeKey, exists := h.clients[ch]
	if exists {
		delete(h.clients, ch)
		if count := h.connectionsByUser[scopeKey]; count <= 1 {
			delete(h.connectionsByUser, scopeKey)
		} else {
			h.connectionsByUser[scopeKey] = count - 1
		}
	}
	h.mu.Unlock()
	if exists {
		close(ch)
	}
}

// broadcast sends an event to all connected SSE clients (non-blocking).
func (h *SSEHub) broadcast(event WorkflowEvent) {
	h.mu.RLock()
	defer h.mu.RUnlock()
	for ch := range h.clients {
		select {
		case ch <- event:
		default:
			// Drop event for slow consumers rather than blocking.
		}
	}
}

// MassTransit fanout exchange names (full .NET type name with colon separator).
const (
	exchangeExecutionRunning = "ReelForge.Shared.IntegrationEvents:WorkflowExecutionRunning"
	exchangeCompleted        = "ReelForge.Shared.IntegrationEvents:WorkflowExecutionCompleted"
	exchangeFailed           = "ReelForge.Shared.IntegrationEvents:WorkflowExecutionFailed"
	exchangeStepStarted      = "ReelForge.Shared.IntegrationEvents:WorkflowStepStarted"
	exchangeStepComplete     = "ReelForge.Shared.IntegrationEvents:WorkflowStepCompleted"
	exchangeStepToolCalled   = "ReelForge.Shared.IntegrationEvents:WorkflowStepToolCalled"
	exchangeStepReasoning    = "ReelForge.Shared.IntegrationEvents:WorkflowStepReasoningCaptured"
	// Ephemeral progress signal for long-running steps (VideoAnalyze/VideoCompile) — see
	// WorkflowStepProgress's doc comment in ReelForge.Shared.IntegrationEvents. Relayed the same
	// way as every other workflow event here; never persisted, so there is no corresponding DB
	// model or migration on either side.
	exchangeStepProgress = "ReelForge.Shared.IntegrationEvents:WorkflowStepProgress"
	// Append-only, sequence-numbered chat turn published once per completed turn in a
	// StepType.EditRoom group-chat run — see WorkflowStepChatTurn's doc comment in
	// ReelForge.Shared.IntegrationEvents.
	exchangeStepChatTurn = "ReelForge.Shared.IntegrationEvents:WorkflowStepChatTurn"

    queueName = "go-api-workflow-events"
)

// StartRabbitMQConsumer connects to RabbitMQ, binds to the workflow event
// exchanges published by the WorkflowEngine, and dispatches events to Hub.
// It reconnects automatically on connection loss.
func StartRabbitMQConsumer() {
	go func() {
		for {
			err := runConsumer()
			if err != nil {
				log.Printf("[rabbitmq] consumer error: %v – reconnecting in 5s", err)
				time.Sleep(5 * time.Second)
			}
		}
	}()
}

func runConsumer() error {
	url := fmt.Sprintf("amqp://%s:%s@%s:%s/",
		config.Cfg.RabbitMQUsername,
		config.Cfg.RabbitMQPassword,
		config.Cfg.RabbitMQHost,
		config.Cfg.RabbitMQPort,
	)

	conn, err := amqp.Dial(url)
	if err != nil {
		return fmt.Errorf("dial: %w", err)
	}
	defer conn.Close()

	ch, err := conn.Channel()
	if err != nil {
		return fmt.Errorf("channel: %w", err)
	}
	defer ch.Close()

	// Declare the durable consumer queue.
	q, err := ch.QueueDeclare(queueName, true, false, false, false, nil)
	if err != nil {
		return fmt.Errorf("queue declare: %w", err)
	}

	// Bind queue to each MassTransit fanout exchange. MassTransit creates these
	// exchanges when the WorkflowEngine publishes the first event; declare them
	// here as well so the binding is idempotent even if we start before the engine.
	for _, exchange := range []string{exchangeExecutionRunning, exchangeCompleted, exchangeFailed, exchangeStepStarted, exchangeStepComplete, exchangeStepToolCalled, exchangeStepReasoning, exchangeStepProgress, exchangeStepChatTurn} {
		if err := ch.ExchangeDeclare(exchange, "fanout", true, false, false, false, nil); err != nil {
			return fmt.Errorf("exchange declare %q: %w", exchange, err)
		}
		if err := ch.QueueBind(q.Name, "", exchange, false, nil); err != nil {
			return fmt.Errorf("queue bind %q: %w", exchange, err)
		}
	}

	msgs, err := ch.Consume(q.Name, "", true, false, false, false, nil)
	if err != nil {
		return fmt.Errorf("consume: %w", err)
	}

	log.Println("[rabbitmq] workflow event consumer started")

	connClose := conn.NotifyClose(make(chan *amqp.Error, 1))

	for {
		select {
		case err := <-connClose:
			if err != nil {
				return fmt.Errorf("connection closed: %w", err)
			}
			return nil

		case msg, ok := <-msgs:
			if !ok {
				return fmt.Errorf("message channel closed")
			}
			dispatchMessage(msg)
		}
	}
}

// dispatchMessage parses an incoming AMQP delivery and forwards it to the SSE hub.
func dispatchMessage(msg amqp.Delivery) {
	// Determine event type from the exchange name.
	var eventType string
	switch msg.Exchange {
	case exchangeExecutionRunning:
		eventType = "execution.running"
	case exchangeCompleted:
		eventType = "execution.completed"
	case exchangeFailed:
		eventType = "execution.failed"
	case exchangeStepStarted:
		eventType = "step.started"
	case exchangeStepComplete:
		eventType = "step.completed"
	case exchangeStepToolCalled:
		eventType = "step.tool-called"
	case exchangeStepReasoning:
		eventType = "step.reasoning"
	case exchangeStepProgress:
		eventType = "step.progress"
	case exchangeStepChatTurn:
		eventType = "step.chat-turn"
	default:
		return
	}

	messagePayload := unwrapMassTransitMessage(msg.Body)
	executionID := extractExecutionID(messagePayload)

	Hub.broadcast(WorkflowEvent{
		Type:        eventType,
		ExecutionID: executionID,
		Timestamp:   time.Now().UTC(),
		Data:        messagePayload,
	})
}

func unwrapMassTransitMessage(body []byte) json.RawMessage {
	var envelope map[string]json.RawMessage
	if err := json.Unmarshal(body, &envelope); err != nil {
		return body
	}

	if message, ok := envelope["message"]; ok && len(message) > 0 {
		return message
	}

	return body
}

func extractExecutionID(payload json.RawMessage) string {
	var message map[string]json.RawMessage
	if err := json.Unmarshal(payload, &message); err != nil {
		return ""
	}

	for _, key := range []string{"executionId", "ExecutionId"} {
		if raw, ok := message[key]; ok {
			var executionID string
			if err := json.Unmarshal(raw, &executionID); err == nil {
				return executionID
			}
		}
	}

	return ""
}
