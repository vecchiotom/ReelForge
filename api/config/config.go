package config

import "os"

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
