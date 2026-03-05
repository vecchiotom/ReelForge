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
		var tokenString string

		// Prefer Authorization header; fall back to httpOnly cookie (used by EventSource/SSE).
		if authHeader := r.Header.Get("Authorization"); strings.HasPrefix(authHeader, "Bearer ") {
			tokenString = strings.TrimPrefix(authHeader, "Bearer ")
		} else if cookie, err := r.Cookie("reelforge_token"); err == nil {
			tokenString = cookie.Value
		}

		if tokenString == "" {
			http.Error(w, `{"error":"missing or invalid authorization header"}`, http.StatusUnauthorized)
			return
		}
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
