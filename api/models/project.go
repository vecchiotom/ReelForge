package models

import (
	"time"

	"github.com/google/uuid"
)

// Project is a read-only GORM view of the projects table
// managed by the .NET Inference service. The Go API never writes to this table.
type Project struct {
	ID          uuid.UUID  `gorm:"type:uuid;primaryKey;column:id"`
	Name        string     `gorm:"column:name"`
	Description *string    `gorm:"column:description"`
	OwnerID     uuid.UUID  `gorm:"column:owner_id"`
	Status      string     `gorm:"column:status"`
	CreatedAt   time.Time  `gorm:"column:created_at"`
	UpdatedAt   time.Time  `gorm:"column:updated_at"`
}

func (Project) TableName() string {
	return "projects"
}
