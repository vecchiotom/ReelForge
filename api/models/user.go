package models

import (
	"time"

	"github.com/google/uuid"
)

type ApplicationUser struct {
	ID                 uuid.UUID `gorm:"type:uuid;primaryKey"`
	Email              string    `gorm:"column:email;uniqueIndex"`
	DisplayName        string    `gorm:"column:display_name"`
	CreatedAt          time.Time `gorm:"column:created_at"`
	PasswordHash       string    `gorm:"column:password_hash"`
	IsAdmin            bool      `gorm:"column:is_admin"`
	MustChangePassword bool      `gorm:"column:must_change_password"`
}

func (ApplicationUser) TableName() string {
	return "application_users"
}
