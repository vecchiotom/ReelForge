package main

import (
	"log"
	"net/http"

	"github.com/vecchiotom/reelforge/config"
	"github.com/vecchiotom/reelforge/database"
	"github.com/vecchiotom/reelforge/handlers"
	"github.com/vecchiotom/reelforge/services"
)

var Version = "dev"

func main() {
	config.Version = Version
	config.Load()

	if _, err := database.Initialize(config.Cfg.DatabaseURL); err != nil {
		log.Fatalf("Failed to connect to database: %v", err)
	}

	services.EnsureAdminExists()

	handlers.RegisterHandlers()

	log.Printf("ReelForge API %s listening on :8080", Version)
	log.Fatal(http.ListenAndServe(":8080", handlers.Router))
}
