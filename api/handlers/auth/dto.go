package auth

type TokenRequest struct {
	Email    string `json:"email"`
	Password string `json:"password"`
}

type TokenResponse struct {
	AccessToken        string `json:"accessToken"`
	TokenType          string `json:"tokenType"`
	ExpiresIn          int    `json:"expiresIn"`
	MustChangePassword bool   `json:"mustChangePassword"`
}

type ChangePasswordRequest struct {
	CurrentPassword string `json:"currentPassword"`
	NewPassword     string `json:"newPassword"`
}
