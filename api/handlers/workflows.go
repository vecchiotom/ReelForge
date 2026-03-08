package handlers

import (
	"encoding/json"
	"fmt"
	"net/http"
	"time"

	"github.com/google/uuid"
	"github.com/gorilla/mux"
	"github.com/vecchiotom/reelforge/database"
	"github.com/vecchiotom/reelforge/middleware"
	"github.com/vecchiotom/reelforge/models"
	"github.com/vecchiotom/reelforge/services"
)

// WorkflowStats holds aggregate execution counts by status.
type WorkflowStats struct {
	Queued    int64 `json:"queued"`
	Active    int64 `json:"active"`
	Completed int64 `json:"completed"`
	Failed    int64 `json:"failed"`
	Total     int64 `json:"total"`
}

// handleWorkflowStats returns aggregate execution statistics queried directly
// from the workflow_executions table (owned by the WorkflowEngine service).
func handleWorkflowStats(w http.ResponseWriter, r *http.Request) {
	type statusCount struct {
		Status string
		Count  int64
	}

	var rows []statusCount
	if err := database.DB.Model(&models.WorkflowExecution{}).
		Select("status, count(*) as count").
		Group("status").
		Scan(&rows).Error; err != nil {
		http.Error(w, `{"error":"failed to query workflow stats"}`, http.StatusInternalServerError)
		return
	}

	stats := WorkflowStats{}
	for _, row := range rows {
		switch row.Status {
		case "Queued":
			stats.Queued = row.Count
		case "Running":
			stats.Active = row.Count
		case "Completed":
			stats.Completed = row.Count
		case "Failed":
			stats.Failed = row.Count
		}
		stats.Total += row.Count
	}

	w.Header().Set("Content-Type", "application/json")
	json.NewEncoder(w).Encode(stats)
}

// handleWorkflowEvents streams real-time workflow execution events to the client
// using Server-Sent Events (SSE). The client must accept text/event-stream.
func handleWorkflowEvents(w http.ResponseWriter, r *http.Request) {
	flusher, ok := w.(http.Flusher)
	if !ok {
		http.Error(w, `{"error":"streaming not supported"}`, http.StatusInternalServerError)
		return
	}

	w.Header().Set("Content-Type", "text/event-stream")
	w.Header().Set("Cache-Control", "no-cache")
	w.Header().Set("Connection", "keep-alive")
	w.Header().Set("X-Accel-Buffering", "no") // Disable nginx buffering

	ch := services.Hub.Subscribe()
	defer services.Hub.Unsubscribe(ch)

	// Send an initial "connected" event so the client knows the stream is live.
	fmt.Fprintf(w, "event: connected\ndata: {\"ts\":%d}\n\n", time.Now().UnixMilli())
	flusher.Flush()

	// Keep-alive ping every 25 seconds to prevent proxy timeouts.
	ticker := time.NewTicker(25 * time.Second)
	defer ticker.Stop()

	for {
		select {
		case <-r.Context().Done():
			return

		case <-ticker.C:
			fmt.Fprintf(w, ": ping\n\n")
			flusher.Flush()

		case event, open := <-ch:
			if !open {
				return
			}
			data, err := json.Marshal(event)
			if err != nil {
				continue
			}
			fmt.Fprintf(w, "event: %s\ndata: %s\n\n", event.Type, data)
			flusher.Flush()
		}
	}
}

// handleExecutionEvents streams real-time workflow events for a SINGLE execution ID.
// Security: verifies user owns the project OR is an admin before allowing access.
func handleExecutionEvents(w http.ResponseWriter, r *http.Request) {
	flusher, ok := w.(http.Flusher)
	if !ok {
		http.Error(w, `{"error":"streaming not supported"}`, http.StatusInternalServerError)
		return
	}

	// Extract route parameters
	vars := mux.Vars(r)
	projectID := vars["projectId"]
	executionID := vars["executionId"]

	// Get user context from auth middleware
	uc, ok := r.Context().Value(middleware.UserContextKey).(middleware.UserContext)
	if !ok {
		http.Error(w, `{"error":"unauthorized"}`, http.StatusUnauthorized)
		return
	}

	// Verify project ownership (admins bypass this check)
	if !uc.IsAdmin {
		var project models.Project
		if err := database.DB.Where("id = ?", projectID).First(&project).Error; err != nil {
			http.Error(w, `{"error":"project not found"}`, http.StatusNotFound)
			return
		}
		if project.OwnerID.String() != uc.UserID {
			http.Error(w, `{"error":"forbidden"}`, http.StatusForbidden)
			return
		}
	}

	w.Header().Set("Content-Type", "text/event-stream")
	w.Header().Set("Cache-Control", "no-cache")
	w.Header().Set("Connection", "keep-alive")
	w.Header().Set("X-Accel-Buffering", "no") // Disable nginx buffering

	ch := services.Hub.Subscribe()
	defer services.Hub.Unsubscribe(ch)

	// Send an initial "connected" event so the client knows the stream is live.
	fmt.Fprintf(w, "event: connected\ndata: {\"ts\":%d}\n\n", time.Now().UnixMilli())
	flusher.Flush()

	// Keep-alive ping every 25 seconds to prevent proxy timeouts.
	ticker := time.NewTicker(25 * time.Second)
	defer ticker.Stop()

	for {
		select {
		case <-r.Context().Done():
			return

		case <-ticker.C:
			fmt.Fprintf(w, ": ping\n\n")
			flusher.Flush()

		case event, open := <-ch:
			if !open {
				return
			}
			// Filter: only stream events for the requested execution ID
			if event.ExecutionID != executionID {
				continue
			}
			data, err := json.Marshal(event)
			if err != nil {
				continue
			}
			fmt.Fprintf(w, "event: %s\ndata: %s\n\n", event.Type, data)
			flusher.Flush()
		}
	}
}
// handleStopExecution allows a user or admin to request that a workflow execution be aborted. After verifying permissions it publishes a stop request message that the WorkflowEngine will consume.
func handleStopExecution(w http.ResponseWriter, r *http.Request) {
    vars := mux.Vars(r)
    projectID := vars["projectId"]
    executionID := vars["executionId"]

    uc, ok := r.Context().Value(middleware.UserContextKey).(middleware.UserContext)
    if !ok {
        http.Error(w, `{"error":"unauthorized"}`, http.StatusUnauthorized)
        return
    }

    // fetch execution record to know associated project
    exec, err := services.GetWorkflowExecution(uuid.MustParse(executionID))
    if err != nil {
        http.Error(w, `{"error":"execution not found"}`, http.StatusNotFound)
        return
    }

    // check ownership unless admin
    if !uc.IsAdmin {
        var project models.Project
        if err := database.DB.Where("id = ?", projectID).First(&project).Error; err != nil {
            http.Error(w, `{"error":"project not found"}`, http.StatusNotFound)
            return
        }
        if project.OwnerID.String() != uc.UserID {
            http.Error(w, `{"error":"forbidden"}`, http.StatusForbidden)
            return
        }
    }

    if err := services.PublishStopRequest(exec.ID.String(), uc.UserID); err != nil {
        http.Error(w, `{"error":"failed to send stop request"}`, http.StatusInternalServerError)
        return
    }

    w.WriteHeader(http.StatusAccepted)
    w.Write([]byte(`{}`))
}
