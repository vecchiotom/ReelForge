package admin

import (
	"time"

	"github.com/google/uuid"
	"github.com/vecchiotom/reelforge/models"
)

type CreateUserRequest struct {
	Email       string `json:"email"`
	DisplayName string `json:"displayName"`
	IsAdmin     bool   `json:"isAdmin"`
}

type CreateUserResponse struct {
	User              UserResponse `json:"user"`
	TemporaryPassword string       `json:"temporaryPassword"`
}

type UpdateUserRequest struct {
	Email         *string `json:"email,omitempty"`
	DisplayName   *string `json:"displayName,omitempty"`
	IsAdmin       *bool   `json:"isAdmin,omitempty"`
	ResetPassword bool    `json:"resetPassword,omitempty"`
}

type UserResponse struct {
	ID                 uuid.UUID `json:"id"`
	Email              string    `json:"email"`
	DisplayName        string    `json:"displayName"`
	IsAdmin            bool      `json:"isAdmin"`
	MustChangePassword bool      `json:"mustChangePassword"`
	CreatedAt          time.Time `json:"createdAt"`
}

func toUserResponse(u *models.ApplicationUser) UserResponse {
	return UserResponse{
		ID:                 u.ID,
		Email:              u.Email,
		DisplayName:        u.DisplayName,
		IsAdmin:            u.IsAdmin,
		MustChangePassword: u.MustChangePassword,
		CreatedAt:          u.CreatedAt,
	}
}
