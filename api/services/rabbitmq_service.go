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
	mu      sync.RWMutex
	clients map[chan WorkflowEvent]struct{}
}

var Hub = &SSEHub{
	clients: make(map[chan WorkflowEvent]struct{}),
}

// Subscribe returns a channel that will receive workflow events. Call Unsubscribe
// when the SSE connection closes.
func (h *SSEHub) Subscribe() chan WorkflowEvent {
	ch := make(chan WorkflowEvent, 32)
	h.mu.Lock()
	h.clients[ch] = struct{}{}
	h.mu.Unlock()
	return ch
}

// Unsubscribe removes the channel and closes it.
func (h *SSEHub) Unsubscribe(ch chan WorkflowEvent) {
	h.mu.Lock()
	delete(h.clients, ch)
	h.mu.Unlock()
	close(ch)
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
    exchangeCompleted        = "ReelForge.Shared.IntegrationEvents:WorkflowExecutionCompleted"
    exchangeFailed           = "ReelForge.Shared.IntegrationEvents:WorkflowExecutionFailed"
    exchangeStepComplete     = "ReelForge.Shared.IntegrationEvents:WorkflowStepCompleted"
    // new event published by Go API when a user requests cancellation
    exchangeStopRequested    = "ReelForge.Shared.IntegrationEvents:WorkflowExecutionStopRequested"

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
	for _, exchange := range []string{exchangeCompleted, exchangeFailed, exchangeStepComplete} {
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
	case exchangeCompleted:
		eventType = "execution.completed"
	case exchangeFailed:
		eventType = "execution.failed"
	case exchangeStepComplete:
		eventType = "step.completed"
	default:
		return
	}

	// Extract executionId from the JSON payload (best-effort).
	var payload map[string]json.RawMessage
	executionID := ""
	if err := json.Unmarshal(msg.Body, &payload); err == nil {
		if raw, ok := payload["executionId"]; ok {
			_ = json.Unmarshal(raw, &executionID)
		}
	}

	Hub.broadcast(WorkflowEvent{
		Type:        eventType,
		ExecutionID: executionID,
		Timestamp:   time.Now().UTC(),
		Data:        msg.Body,
	})
}
// PublishStopRequest sends a user-initiated cancel request to RabbitMQ so the
// WorkflowEngine can abort the execution. It publishes to the same fanout
// exchange that MassTransit consumers listen on.
func PublishStopRequest(executionID, requestedBy string) error {
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

    // ensure exchange exists as fanout
    if err := ch.ExchangeDeclare(exchangeStopRequested, "fanout", true, false, false, false, nil); err != nil {
        return fmt.Errorf("exchange declare: %w", err)
    }

    payload := map[string]string{
        "executionId": executionID,
        "requestedBy": requestedBy,
        "requestedAt": time.Now().UTC().Format(time.RFC3339),
    }
    body, _ := json.Marshal(payload)

    if err := ch.Publish(exchangeStopRequested, "", false, false, amqp.Publishing{
        ContentType: "application/json",
        Body:        body,
    }); err != nil {
        return fmt.Errorf("publish stop request: %w", err)
    }
    return nil
}