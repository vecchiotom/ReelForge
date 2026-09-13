package config

import (
	"log"
	"os"
	"strconv"
)

var Version = "dev"

type AppConfig struct {
	DatabaseURL  string
	InferenceURL string

	JWTSigningKey string
	JWTIssuer     string
	JWTAudience   string

	SMTPHost     string
	SMTPPort     string
	SMTPUsername string
	SMTPPassword string
	SMTPFrom     string

	AdminEmail    string
	AdminPassword string

	RabbitMQHost     string
	RabbitMQPort     string
	RabbitMQUsername string
	RabbitMQPassword string

	// CookieSecure sets the Secure flag on the reelforge_token/reelforge_user
	// cookies. Leave false for http://localhost development — browsers
	// silently drop Secure cookies over plain HTTP, which would break login
	// entirely. Set COOKIE_SECURE=true once nginx is actually serving TLS
	// (see docs/tls.md).
	CookieSecure bool
}

var Cfg AppConfig

func Load() {
	Cfg = AppConfig{
		DatabaseURL:  getEnv("DATABASE_URL", "postgres://postgres:postgres@localhost:5432/reelforge?sslmode=disable"),
		InferenceURL: getEnv("INFERENCE_URL", "http://localhost:5200"),

		JWTSigningKey: getEnv("JWT_SIGNING_KEY", ""),
		JWTIssuer:     getEnv("JWT_ISSUER", "reelforge-api"),
		JWTAudience:   getEnv("JWT_AUDIENCE", "reelforge-inference"),

		SMTPHost:     getEnv("SMTP_HOST", ""),
		SMTPPort:     getEnv("SMTP_PORT", "587"),
		SMTPUsername: getEnv("SMTP_USERNAME", ""),
		SMTPPassword: getEnv("SMTP_PASSWORD", ""),
		SMTPFrom:     getEnv("SMTP_FROM", ""),

		AdminEmail:    getEnv("ADMIN_EMAIL", "admin@reelforge.local"),
		AdminPassword: getEnv("ADMIN_PASSWORD", ""),

		RabbitMQHost:     getEnv("RABBITMQ_HOST", "localhost"),
		RabbitMQPort:     getEnv("RABBITMQ_PORT", "5672"),
		RabbitMQUsername: getEnv("RABBITMQ_USER", "guest"),
		RabbitMQPassword: getEnv("RABBITMQ_PASSWORD", "guest"),

		CookieSecure: getBoolEnv("COOKIE_SECURE", false),
	}

	if len(Cfg.JWTSigningKey) < 32 {
		log.Fatalf("JWT_SIGNING_KEY must be set to a random value of at least 32 characters (got %d); generate one with: openssl rand -hex 32", len(Cfg.JWTSigningKey))
	}
}

func SMTPConfigured() bool {
	return Cfg.SMTPHost != "" && Cfg.SMTPFrom != ""
}

func getEnv(key, fallback string) string {
	if v := os.Getenv(key); v != "" {
		return v
	}
	return fallback
}

func getBoolEnv(key string, fallback bool) bool {
	if v := os.Getenv(key); v != "" {
		if parsed, err := strconv.ParseBool(v); err == nil {
			return parsed
		}
	}
	return fallback
}
