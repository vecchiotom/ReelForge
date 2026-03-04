package services

import (
	"crypto/rand"
	"fmt"
	"log"
	"math/big"
	"time"

	"github.com/google/uuid"
	"github.com/vecchiotom/reelforge/config"
	"github.com/vecchiotom/reelforge/database"
	"github.com/vecchiotom/reelforge/models"
	"golang.org/x/crypto/bcrypt"
)

const otpChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%"

func generateOTP(length int) (string, error) {
	b := make([]byte, length)
	for i := range b {
		n, err := rand.Int(rand.Reader, big.NewInt(int64(len(otpChars))))
		if err != nil {
			return "", err
		}
		b[i] = otpChars[n.Int64()]
	}
	return string(b), nil
}

func CreateUser(email, displayName string, isAdmin bool) (*models.ApplicationUser, string, error) {
	otp, err := generateOTP(16)
	if err != nil {
		return nil, "", fmt.Errorf("generate OTP: %w", err)
	}

	hash, err := bcrypt.GenerateFromPassword([]byte(otp), bcrypt.DefaultCost)
	if err != nil {
		return nil, "", fmt.Errorf("hash password: %w", err)
	}

	user := models.ApplicationUser{
		ID:                 uuid.New(),
		Email:              email,
		DisplayName:        displayName,
		CreatedAt:          time.Now().UTC(),
		PasswordHash:       string(hash),
		IsAdmin:            isAdmin,
		MustChangePassword: true,
	}

	if err := database.DB.Create(&user).Error; err != nil {
		return nil, "", fmt.Errorf("create user: %w", err)
	}

	return &user, otp, nil
}

func GetUserByID(id uuid.UUID) (*models.ApplicationUser, error) {
	var user models.ApplicationUser
	if err := database.DB.First(&user, "id = ?", id).Error; err != nil {
		return nil, err
	}
	return &user, nil
}

func GetUserByEmail(email string) (*models.ApplicationUser, error) {
	var user models.ApplicationUser
	if err := database.DB.First(&user, "email = ?", email).Error; err != nil {
		return nil, err
	}
	return &user, nil
}

func ListUsers() ([]models.ApplicationUser, error) {
	var users []models.ApplicationUser
	if err := database.DB.Find(&users).Error; err != nil {
		return nil, err
	}
	return users, nil
}

func UpdateUser(id uuid.UUID, fields map[string]interface{}) (*models.ApplicationUser, error) {
	var user models.ApplicationUser
	if err := database.DB.First(&user, "id = ?", id).Error; err != nil {
		return nil, err
	}
	if err := database.DB.Model(&user).Updates(fields).Error; err != nil {
		return nil, err
	}
	return &user, nil
}

func DeleteUser(id uuid.UUID) error {
	return database.DB.Delete(&models.ApplicationUser{}, "id = ?", id).Error
}

func ValidatePassword(user *models.ApplicationUser, password string) bool {
	return bcrypt.CompareHashAndPassword([]byte(user.PasswordHash), []byte(password)) == nil
}

func ChangePassword(userID uuid.UUID, newPassword string) error {
	hash, err := bcrypt.GenerateFromPassword([]byte(newPassword), bcrypt.DefaultCost)
	if err != nil {
		return fmt.Errorf("hash password: %w", err)
	}
	return database.DB.Model(&models.ApplicationUser{}).
		Where("id = ?", userID).
		Updates(map[string]interface{}{
			"password_hash":        string(hash),
			"must_change_password": false,
		}).Error
}

func ResetPassword(id uuid.UUID) (string, error) {
	otp, err := generateOTP(16)
	if err != nil {
		return "", fmt.Errorf("generate OTP: %w", err)
	}

	hash, err := bcrypt.GenerateFromPassword([]byte(otp), bcrypt.DefaultCost)
	if err != nil {
		return "", fmt.Errorf("hash password: %w", err)
	}

	err = database.DB.Model(&models.ApplicationUser{}).
		Where("id = ?", id).
		Updates(map[string]interface{}{
			"password_hash":        string(hash),
			"must_change_password": true,
		}).Error
	if err != nil {
		return "", err
	}

	return otp, nil
}

func EnsureAdminExists() {
	var count int64
	database.DB.Model(&models.ApplicationUser{}).Count(&count)
	if count > 0 {
		return
	}

	email := config.Cfg.AdminEmail
	password := config.Cfg.AdminPassword

	if password == "" {
		generated, err := generateOTP(16)
		if err != nil {
			log.Fatalf("Failed to generate admin password: %v", err)
		}
		password = generated
	}

	hash, err := bcrypt.GenerateFromPassword([]byte(password), bcrypt.DefaultCost)
	if err != nil {
		log.Fatalf("Failed to hash admin password: %v", err)
	}

	admin := models.ApplicationUser{
		ID:                 uuid.New(),
		Email:              email,
		DisplayName:        "Admin",
		CreatedAt:          time.Now().UTC(),
		PasswordHash:       string(hash),
		IsAdmin:            true,
		MustChangePassword: config.Cfg.AdminPassword == "",
	}

	if err := database.DB.Create(&admin).Error; err != nil {
		log.Fatalf("Failed to create admin user: %v", err)
	}

	log.Printf("=== Initial admin user created ===")
	log.Printf("Email:    %s", email)
	log.Printf("Password: %s", password)
	if admin.MustChangePassword {
		log.Printf("(Password change required on first login)")
	}
	log.Printf("==================================")
}
