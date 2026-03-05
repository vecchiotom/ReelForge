package auth

import (
	"encoding/json"
	"fmt"
	"net/http"
	"net/url"

	"github.com/google/uuid"
	"github.com/gorilla/mux"
	"github.com/vecchiotom/reelforge/middleware"
	"github.com/vecchiotom/reelforge/services"
)

func RegisterAuthRoutes(router *mux.Router) {
	router.HandleFunc("/api/v1/auth/token", handleToken).Methods("POST")
}

func RegisterAuthenticatedRoutes(router *mux.Router) {
	router.HandleFunc("/api/v1/auth/change-password", handleChangePassword).Methods("POST")
}

func handleToken(w http.ResponseWriter, r *http.Request) {
	var req TokenRequest
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		http.Error(w, `{"error":"invalid request body"}`, http.StatusBadRequest)
		return
	}

	if req.Email == "" || req.Password == "" {
		http.Error(w, `{"error":"email and password are required"}`, http.StatusBadRequest)
		return
	}

	user, err := services.GetUserByEmail(req.Email)
	if err != nil {
		http.Error(w, `{"error":"invalid credentials"}`, http.StatusUnauthorized)
		return
	}

	if !services.ValidatePassword(user, req.Password) {
		http.Error(w, `{"error":"invalid credentials"}`, http.StatusUnauthorized)
		return
	}

	token, err := services.GenerateToken(user)
	if err != nil {
		http.Error(w, `{"error":"failed to generate token"}`, http.StatusInternalServerError)
		return
	}

	// Set httpOnly cookie with the JWT token
	http.SetCookie(w, &http.Cookie{
		Name:     "reelforge_token",
		Value:    token,
		Path:     "/",
		MaxAge:   86400,
		HttpOnly: true,
		SameSite: http.SameSiteLaxMode,
	})

	// Set readable cookie with user info for frontend UI state
	userInfo := fmt.Sprintf(`{"email":%q,"isAdmin":%t,"mustChangePassword":%t}`,
		user.Email, user.IsAdmin, user.MustChangePassword)
	http.SetCookie(w, &http.Cookie{
		Name:     "reelforge_user",
		Value:    url.QueryEscape(userInfo),
		Path:     "/",
		MaxAge:   86400,
		HttpOnly: false,
		SameSite: http.SameSiteLaxMode,
	})

	w.Header().Set("Content-Type", "application/json")
	json.NewEncoder(w).Encode(TokenResponse{
		AccessToken:        token,
		TokenType:          "Bearer",
		ExpiresIn:          86400,
		Email:              user.Email,
		IsAdmin:            user.IsAdmin,
		MustChangePassword: user.MustChangePassword,
	})
}

func handleChangePassword(w http.ResponseWriter, r *http.Request) {
	uc, ok := middleware.GetUserContext(r)
	if !ok {
		http.Error(w, `{"error":"unauthorized"}`, http.StatusUnauthorized)
		return
	}

	var req ChangePasswordRequest
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		http.Error(w, `{"error":"invalid request body"}`, http.StatusBadRequest)
		return
	}

	if req.CurrentPassword == "" || req.NewPassword == "" {
		http.Error(w, `{"error":"current_password and new_password are required"}`, http.StatusBadRequest)
		return
	}

	if len(req.NewPassword) < 8 {
		http.Error(w, `{"error":"new password must be at least 8 characters"}`, http.StatusBadRequest)
		return
	}

	userID, err := uuid.Parse(uc.UserID)
	if err != nil {
		http.Error(w, `{"error":"invalid user ID"}`, http.StatusInternalServerError)
		return
	}

	user, err := services.GetUserByID(userID)
	if err != nil {
		http.Error(w, `{"error":"user not found"}`, http.StatusNotFound)
		return
	}

	if !services.ValidatePassword(user, req.CurrentPassword) {
		http.Error(w, `{"error":"current password is incorrect"}`, http.StatusUnauthorized)
		return
	}

	if err := services.ChangePassword(userID, req.NewPassword); err != nil {
		http.Error(w, `{"error":"failed to change password"}`, http.StatusInternalServerError)
		return
	}

	w.Header().Set("Content-Type", "application/json")
	json.NewEncoder(w).Encode(map[string]string{"message": "password changed successfully"})
}
