package services

import (
	"github.com/google/uuid"
	"github.com/vecchiotom/reelforge/database"
	"github.com/vecchiotom/reelforge/models"
)

// ListWorkflowExecutions returns all executions ordered by most-recent first.
// Pass projectID as non-nil to filter by project.
func ListWorkflowExecutions(projectID *uuid.UUID, limit, offset int) ([]models.WorkflowExecution, int64, error) {
	var executions []models.WorkflowExecution
	var total int64

	q := database.DB.Model(&models.WorkflowExecution{})
	if projectID != nil {
		q = q.Where("project_id = ?", *projectID)
	}

	if err := q.Count(&total).Error; err != nil {
		return nil, 0, err
	}

	if err := q.Order("started_at DESC NULLS LAST").
		Limit(limit).Offset(offset).
		Find(&executions).Error; err != nil {
		return nil, 0, err
	}

	return executions, total, nil
}

// GetWorkflowExecution returns a single execution by ID.
func GetWorkflowExecution(id uuid.UUID) (*models.WorkflowExecution, error) {
	var execution models.WorkflowExecution
	if err := database.DB.First(&execution, "id = ?", id).Error; err != nil {
		return nil, err
	}
	return &execution, nil
}
