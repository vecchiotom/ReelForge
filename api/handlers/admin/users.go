package admin

import (
	"encoding/json"
	"net/http"

	"github.com/google/uuid"
	"github.com/gorilla/mux"
	"github.com/vecchiotom/reelforge/models"
	"github.com/vecchiotom/reelforge/services"
)

func RegisterAdminRoutes(router *mux.Router) {
	router.HandleFunc("/users", handleCreateUser).Methods("POST")
	router.HandleFunc("/users", handleListUsers).Methods("GET")
	router.HandleFunc("/users/{id}", handleGetUser).Methods("GET")
	router.HandleFunc("/users/{id}", handleUpdateUser).Methods("PUT")
	router.HandleFunc("/users/{id}", handleDeleteUser).Methods("DELETE")
}

func handleCreateUser(w http.ResponseWriter, r *http.Request) {
	var req CreateUserRequest
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		http.Error(w, `{"error":"invalid request body"}`, http.StatusBadRequest)
		return
	}

	if req.Email == "" || req.DisplayName == "" {
		http.Error(w, `{"error":"email and display_name are required"}`, http.StatusBadRequest)
		return
	}

	user, otp, err := services.CreateUser(req.Email, req.DisplayName, req.IsAdmin)
	if err != nil {
		http.Error(w, `{"error":"failed to create user: `+err.Error()+`"}`, http.StatusInternalServerError)
		return
	}

	go services.SendWelcomeEmail(user.Email, user.DisplayName, otp)

	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(http.StatusCreated)
	json.NewEncoder(w).Encode(CreateUserResponse{
		User:             toUserResponse(user),
		TemporaryPassword: otp,
	})
}

func handleListUsers(w http.ResponseWriter, r *http.Request) {
	users, err := services.ListUsers()
	if err != nil {
		http.Error(w, `{"error":"failed to list users"}`, http.StatusInternalServerError)
		return
	}

	resp := make([]UserResponse, len(users))
	for i := range users {
		resp[i] = toUserResponse(&users[i])
	}

	w.Header().Set("Content-Type", "application/json")
	json.NewEncoder(w).Encode(resp)
}

func handleGetUser(w http.ResponseWriter, r *http.Request) {
	id, err := uuid.Parse(mux.Vars(r)["id"])
	if err != nil {
		http.Error(w, `{"error":"invalid user ID"}`, http.StatusBadRequest)
		return
	}

	user, err := services.GetUserByID(id)
	if err != nil {
		http.Error(w, `{"error":"user not found"}`, http.StatusNotFound)
		return
	}

	w.Header().Set("Content-Type", "application/json")
	json.NewEncoder(w).Encode(toUserResponse(user))
}

func handleUpdateUser(w http.ResponseWriter, r *http.Request) {
	id, err := uuid.Parse(mux.Vars(r)["id"])
	if err != nil {
		http.Error(w, `{"error":"invalid user ID"}`, http.StatusBadRequest)
		return
	}

	var req UpdateUserRequest
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		http.Error(w, `{"error":"invalid request body"}`, http.StatusBadRequest)
		return
	}

	// Handle password reset first if requested
	if req.ResetPassword {
		otp, err := services.ResetPassword(id)
		if err != nil {
			http.Error(w, `{"error":"failed to reset password"}`, http.StatusInternalServerError)
			return
		}

		user, err := services.GetUserByID(id)
		if err != nil {
			http.Error(w, `{"error":"user not found"}`, http.StatusNotFound)
			return
		}

		go services.SendWelcomeEmail(user.Email, user.DisplayName, otp)
	}

	// Build update fields
	fields := make(map[string]interface{})
	if req.Email != nil {
		fields["email"] = *req.Email
	}
	if req.DisplayName != nil {
		fields["display_name"] = *req.DisplayName
	}
	if req.IsAdmin != nil {
		fields["is_admin"] = *req.IsAdmin
	}

	var user *models.ApplicationUser
	if len(fields) > 0 {
		user, err = services.UpdateUser(id, fields)
		if err != nil {
			http.Error(w, `{"error":"failed to update user"}`, http.StatusInternalServerError)
			return
		}
	} else {
		user, err = services.GetUserByID(id)
		if err != nil {
			http.Error(w, `{"error":"user not found"}`, http.StatusNotFound)
			return
		}
	}

	w.Header().Set("Content-Type", "application/json")
	json.NewEncoder(w).Encode(toUserResponse(user))
}

func handleDeleteUser(w http.ResponseWriter, r *http.Request) {
	id, err := uuid.Parse(mux.Vars(r)["id"])
	if err != nil {
		http.Error(w, `{"error":"invalid user ID"}`, http.StatusBadRequest)
		return
	}

	if err := services.DeleteUser(id); err != nil {
		http.Error(w, `{"error":"failed to delete user"}`, http.StatusInternalServerError)
		return
	}

	w.WriteHeader(http.StatusNoContent)
}
