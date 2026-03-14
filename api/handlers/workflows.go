package handlers

import (
	"encoding/json"
	"errors"
	"fmt"
	"net/http"
	"time"

	"github.com/gorilla/mux"
	"github.com/vecchiotom/reelforge/database"
	"github.com/vecchiotom/reelforge/middleware"
	"github.com/vecchiotom/reelforge/models"
	"github.com/vecchiotom/reelforge/services"
	"gorm.io/gorm"
)

// WorkflowStats holds aggregate execution counts by status.
type WorkflowStats struct {
	Queued    int64 `json:"queued"`
	Active    int64 `json:"active"`
	Completed int64 `json:"completed"`
	Failed    int64 `json:"failed"`
	Total     int64 `json:"total"`
}

func userOwnsExecution(uc middleware.UserContext, execution *models.WorkflowExecution) bool {
	if uc.IsAdmin {
		return true
	}

	if execution.InitiatedByUserID != nil {
		return execution.InitiatedByUserID.String() == uc.UserID
	}

	var project models.Project
	if err := database.DB.Select("owner_id").Where("id = ?", execution.ProjectID).First(&project).Error; err != nil {
		return false
	}

	return project.OwnerID.String() == uc.UserID
}

func canUserStreamExecutionByID(uc middleware.UserContext, executionID string) bool {
	if uc.IsAdmin {
		return true
	}

	var execution models.WorkflowExecution
	if err := database.DB.Select("id", "project_id", "initiated_by_user_id").Where("id = ?", executionID).First(&execution).Error; err != nil {
		return false
	}

	return userOwnsExecution(uc, &execution)
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

	uc, ok := r.Context().Value(middleware.UserContextKey).(middleware.UserContext)
	if !ok {
		http.Error(w, `{"error":"unauthorized"}`, http.StatusUnauthorized)
		return
	}

	w.Header().Set("Content-Type", "text/event-stream")
	w.Header().Set("Cache-Control", "no-cache")
	w.Header().Set("Connection", "keep-alive")
	w.Header().Set("X-Accel-Buffering", "no") // Disable nginx buffering

	ch := services.Hub.Subscribe()
	defer services.Hub.Unsubscribe(ch)
	visibilityCache := make(map[string]bool)

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

			if !uc.IsAdmin {
				allowed, seen := visibilityCache[event.ExecutionID]
				if !seen {
					allowed = canUserStreamExecutionByID(uc, event.ExecutionID)
					visibilityCache[event.ExecutionID] = allowed
				}
				if !allowed {
					continue
				}
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
	workflowID := vars["workflowId"]
	executionID := vars["executionId"]

	// Get user context from auth middleware
	uc, ok := r.Context().Value(middleware.UserContextKey).(middleware.UserContext)
	if !ok {
		http.Error(w, `{"error":"unauthorized"}`, http.StatusUnauthorized)
		return
	}

	// Validate execution belongs to the requested project + workflow path.
	var execution models.WorkflowExecution
	if err := database.DB.Where("id = ? AND project_id = ? AND workflow_definition_id = ?", executionID, projectID, workflowID).First(&execution).Error; err != nil {
		if errors.Is(err, gorm.ErrRecordNotFound) {
			http.Error(w, `{"error":"execution not found"}`, http.StatusNotFound)
			return
		}
		http.Error(w, `{"error":"failed to validate execution"}`, http.StatusInternalServerError)
		return
	}

	// Verify execution ownership (admins bypass this check).
	if !userOwnsExecution(uc, &execution) {
		http.Error(w, `{"error":"forbidden"}`, http.StatusForbidden)
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
