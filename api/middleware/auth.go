package middleware

import (
	"context"
	"net/http"
	"strings"

	"github.com/vecchiotom/reelforge/services"
)

type contextKey string

const UserContextKey contextKey = "user"

type UserContext struct {
	UserID  string
	Email   string
	IsAdmin bool
}

func Auth(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		authHeader := r.Header.Get("Authorization")
		if authHeader == "" || !strings.HasPrefix(authHeader, "Bearer ") {
			http.Error(w, `{"error":"missing or invalid authorization header"}`, http.StatusUnauthorized)
			return
		}

		tokenString := strings.TrimPrefix(authHeader, "Bearer ")
		claims, err := services.ValidateToken(tokenString)
		if err != nil {
			http.Error(w, `{"error":"invalid token"}`, http.StatusUnauthorized)
			return
		}

		uc := UserContext{
			UserID:  claims.UserID,
			Email:   claims.Email,
			IsAdmin: claims.IsAdmin,
		}

		ctx := context.WithValue(r.Context(), UserContextKey, uc)
		next.ServeHTTP(w, r.WithContext(ctx))
	})
}

func GetUserContext(r *http.Request) (UserContext, bool) {
	uc, ok := r.Context().Value(UserContextKey).(UserContext)
	return uc, ok
}
