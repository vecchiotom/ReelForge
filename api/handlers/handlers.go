package handlers

import (
	"github.com/gorilla/mux"
	"github.com/vecchiotom/reelforge/handlers/admin"
	"github.com/vecchiotom/reelforge/handlers/auth"
	"github.com/vecchiotom/reelforge/handlers/health"
	"github.com/vecchiotom/reelforge/middleware"
)

var Router *mux.Router

func NewHandler() *mux.Router {
	router := mux.NewRouter()
	Router = router
	return router
}

func RegisterHandlers() {
	if Router == nil {
		NewHandler()
	}

	// Public routes (no auth required)
	health.RegisterHealthRoutes(Router)
	auth.RegisterAuthRoutes(Router)

	// Authenticated routes
	authenticated := Router.PathPrefix("").Subrouter()
	authenticated.Use(middleware.Auth)
	auth.RegisterAuthenticatedRoutes(authenticated)

	// Admin routes (auth + admin required)
	adminRouter := Router.PathPrefix("/api/v1/admin").Subrouter()
	adminRouter.Use(middleware.Auth)
	adminRouter.Use(middleware.Admin)
	admin.RegisterAdminRoutes(adminRouter)
}
