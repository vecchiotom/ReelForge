package services

import (
	"fmt"
	"log"
	"net/smtp"

	"github.com/vecchiotom/reelforge/config"
)

func SendWelcomeEmail(toEmail, displayName, otp string) {
	if !config.SMTPConfigured() {
		log.Printf("[SMTP not configured] Welcome email for %s — temporary password: %s", toEmail, otp)
		return
	}

	subject := "Welcome to ReelForge"
	body := fmt.Sprintf(
		"Hello %s,\r\n\r\nYour ReelForge account has been created.\r\n\r\nYour temporary password is: %s\r\n\r\nPlease change your password on first login.\r\n\r\n— ReelForge",
		displayName, otp,
	)

	msg := fmt.Sprintf(
		"From: %s\r\nTo: %s\r\nSubject: %s\r\nMIME-Version: 1.0\r\nContent-Type: text/plain; charset=\"UTF-8\"\r\n\r\n%s",
		config.Cfg.SMTPFrom, toEmail, subject, body,
	)

	addr := fmt.Sprintf("%s:%s", config.Cfg.SMTPHost, config.Cfg.SMTPPort)

	var auth smtp.Auth
	if config.Cfg.SMTPUsername != "" {
		auth = smtp.PlainAuth("", config.Cfg.SMTPUsername, config.Cfg.SMTPPassword, config.Cfg.SMTPHost)
	}

	if err := smtp.SendMail(addr, auth, config.Cfg.SMTPFrom, []string{toEmail}, []byte(msg)); err != nil {
		log.Printf("Failed to send welcome email to %s: %v", toEmail, err)
		log.Printf("Temporary password for %s: %s", toEmail, otp)
	}
}
