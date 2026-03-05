package models

import (
	"time"

	"github.com/google/uuid"
)

// WorkflowExecution is a read-only GORM view of the workflow_executions table
// managed by the .NET WorkflowEngine service. The Go API never writes to this table.
type WorkflowExecution struct {
	ID                   uuid.UUID  `gorm:"type:uuid;primaryKey;column:id"`
	WorkflowDefinitionID uuid.UUID  `gorm:"column:workflow_definition_id"`
	ProjectID            uuid.UUID  `gorm:"column:project_id"`
	Status               string     `gorm:"column:status"`
	StartedAt            *time.Time `gorm:"column:started_at"`
	CompletedAt          *time.Time `gorm:"column:completed_at"`
	IterationCount       int        `gorm:"column:iteration_count"`
	CorrelationId        string     `gorm:"column:correlation_id"`
	InitiatedByUserID    *uuid.UUID `gorm:"column:initiated_by_user_id"`
	ErrorMessage         *string    `gorm:"column:error_message"`
}

func (WorkflowExecution) TableName() string {
	return "workflow_executions"
}
