package health

import (
	"encoding/json"
	"net/http"

	"github.com/gorilla/mux"
	"github.com/vecchiotom/reelforge/config"
)

func RegisterHealthRoutes(router *mux.Router) {
	router.HandleFunc("/health", healthCheckHandler).Methods("GET")
}

func healthCheckHandler(w http.ResponseWriter, r *http.Request) {
	w.WriteHeader(http.StatusOK)
	json.NewEncoder(w).Encode(map[string]string{"status": "ok", "version": config.Version})
}
