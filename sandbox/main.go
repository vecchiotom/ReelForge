package main

import (
	"context"
	"encoding/base64"
	"encoding/json"
	"errors"
	"fmt"
	"log"
	"net/http"
	"os"
	"os/exec"
	"path/filepath"
	"regexp"
	"strconv"
	"strings"
	"sync"
	"time"

	"github.com/google/uuid"
	"github.com/gorilla/mux"
)

var Version = "dev"

type appConfig struct {
	Port         string
	SandboxImage string
	SandboxRoot  string
	SandboxTTL   time.Duration
	ExecTimeout  time.Duration
	MemoryLimit  string
	CPULimit     string
	PIDsLimit    int
}

func loadConfig() appConfig {
	return appConfig{
		Port:         getEnv("PORT", "8080"),
		SandboxImage: getEnv("SANDBOX_IMAGE", "reelforge-sandbox-executor:local"),
		SandboxRoot:  getEnv("SANDBOX_ROOT", "/var/lib/reelforge/sandboxes"),
		SandboxTTL:   getDurationEnv("SANDBOX_TTL", time.Hour),
		ExecTimeout:  getDurationEnv("SANDBOX_EXEC_TIMEOUT", 5*time.Minute),
		MemoryLimit:  getEnv("SANDBOX_MEMORY_LIMIT", "2g"),
		CPULimit:     getEnv("SANDBOX_CPU_LIMIT", "2"),
		PIDsLimit:    getIntEnv("SANDBOX_PIDS_LIMIT", 256),
	}
}

func getEnv(key, fallback string) string {
	if v := os.Getenv(key); v != "" {
		return v
	}
	return fallback
}

func getIntEnv(key string, fallback int) int {
	v := os.Getenv(key)
	if v == "" {
		return fallback
	}
	n, err := strconv.Atoi(v)
	if err != nil || n <= 0 {
		return fallback
	}
	return n
}

func getDurationEnv(key string, fallback time.Duration) time.Duration {
	v := os.Getenv(key)
	if v == "" {
		return fallback
	}
	d, err := time.ParseDuration(v)
	if err != nil || d <= 0 {
		return fallback
	}
	return d
}

type sandbox struct {
	ID                  string    `json:"id"`
	WorkflowExecutionID string    `json:"workflowExecutionId"`
	ContainerName       string    `json:"containerName"`
	WorkspacePath       string    `json:"workspacePath"`
	CreatedAt           time.Time `json:"createdAt"`
	LastActivity        time.Time `json:"lastActivity"`
}

type sandboxManager struct {
	cfg            appConfig
	mu             sync.RWMutex
	sandboxes      map[string]*sandbox
	executionIndex map[string]string
}

var (
	errNotFound      = errors.New("sandbox not found")
	errInvalidPath   = errors.New("invalid path")
	errBadExec       = errors.New("command not allowed")
	errBadExecution  = errors.New("invalid workflowExecutionId")
	errBadPackage    = errors.New("invalid package name")
	allowedNPMScript = map[string]struct{}{
		"build":        {},
		"render":       {},
		"typecheck":    {},
		"compositions": {},
		"lint":         {},
	}
	// npmPackageNameRe matches valid npm package names including scoped packages.
	npmPackageNameRe = regexp.MustCompile(`^(@[a-z0-9\-_]+/)?[a-z0-9\-_.]+(@[a-zA-Z0-9.\-_]+)?$`)
)

func newSandboxManager(cfg appConfig) *sandboxManager {
	return &sandboxManager{
		cfg:            cfg,
		sandboxes:      make(map[string]*sandbox),
		executionIndex: make(map[string]string),
	}
}

func validateWorkflowExecutionID(workflowExecutionID string) error {
	if _, err := uuid.Parse(workflowExecutionID); err != nil {
		return errBadExecution
	}
	return nil
}

func (m *sandboxManager) create(workflowExecutionID string) (*sandbox, bool, error) {
	if err := validateWorkflowExecutionID(workflowExecutionID); err != nil {
		return nil, false, err
	}

	m.mu.Lock()
	if existingID, ok := m.executionIndex[workflowExecutionID]; ok {
		if existing, found := m.sandboxes[existingID]; found {
			existing.LastActivity = time.Now().UTC()
			copy := *existing
			m.mu.Unlock()
			return &copy, false, nil
		}
	}
	m.mu.Unlock()

	id := uuid.NewString()
	workspace := filepath.Join(m.cfg.SandboxRoot, id)
	if err := os.MkdirAll(workspace, 0o777); err != nil {
		return nil, false, err
	}
	_ = os.Chmod(workspace, 0o777)

	containerName := "rf-sbx-" + id
	initCommand := "if [ ! -f /workspace/package.json ]; then cp -a /opt/remotion-template/. /workspace/; fi; sleep infinity"
	args := []string{
		"run", "-d",
		"--name", containerName,
		"--network", "none",
		"--read-only",
		"--tmpfs", "/tmp:rw,nosuid,nodev,size=256m",
		"--memory", m.cfg.MemoryLimit,
		"--cpus", m.cfg.CPULimit,
		"--pids-limit", strconv.Itoa(m.cfg.PIDsLimit),
		"--security-opt", "no-new-privileges",
		"--cap-drop", "ALL",
		"--user", "node",
		"-v", workspace + ":/workspace",
		"-w", "/workspace",
		m.cfg.SandboxImage,
		"sh", "-lc", initCommand,
	}
	if _, err := runDocker(context.Background(), args...); err != nil {
		_ = os.RemoveAll(workspace)
		return nil, false, fmt.Errorf("failed to start sandbox container: %w", err)
	}

	now := time.Now().UTC()
	sb := &sandbox{
		ID:                  id,
		WorkflowExecutionID: workflowExecutionID,
		ContainerName:       containerName,
		WorkspacePath:       workspace,
		CreatedAt:           now,
		LastActivity:        now,
	}

	m.mu.Lock()
	m.sandboxes[id] = sb
	m.executionIndex[workflowExecutionID] = id
	m.mu.Unlock()
	return sb, true, nil
}

func (m *sandboxManager) list() []*sandbox {
	m.mu.RLock()
	defer m.mu.RUnlock()

	out := make([]*sandbox, 0, len(m.sandboxes))
	for _, sb := range m.sandboxes {
		copy := *sb
		out = append(out, &copy)
	}
	return out
}

func (m *sandboxManager) getByExecution(workflowExecutionID string) (*sandbox, error) {
	if err := validateWorkflowExecutionID(workflowExecutionID); err != nil {
		return nil, err
	}
	m.mu.RLock()
	sandboxID, ok := m.executionIndex[workflowExecutionID]
	if !ok {
		m.mu.RUnlock()
		return nil, errNotFound
	}
	sb, ok := m.sandboxes[sandboxID]
	m.mu.RUnlock()
	if !ok {
		return nil, errNotFound
	}

	copy := *sb
	return &copy, nil
}

func (m *sandboxManager) touch(workflowExecutionID string) {
	m.mu.Lock()
	if sandboxID, ok := m.executionIndex[workflowExecutionID]; ok {
		if sb, found := m.sandboxes[sandboxID]; found {
			sb.LastActivity = time.Now().UTC()
		}
	}
	m.mu.Unlock()
}

func (m *sandboxManager) deleteByExecution(workflowExecutionID string) error {
	sb, err := m.getByExecution(workflowExecutionID)
	if err != nil {
		return err
	}
	return m.deleteInternal(sb.ID, sb.WorkflowExecutionID, sb.ContainerName, sb.WorkspacePath)
}

func (m *sandboxManager) deleteInternal(id, workflowExecutionID, containerName, workspace string) error {
	_, _ = runDocker(context.Background(), "rm", "-f", containerName)
	if err := os.RemoveAll(workspace); err != nil {
		return err
	}

	m.mu.Lock()
	delete(m.sandboxes, id)
	delete(m.executionIndex, workflowExecutionID)
	m.mu.Unlock()
	return nil
}

func (m *sandboxManager) runExec(workflowExecutionID string, req execRequest) (string, error) {
	sb, err := m.getByExecution(workflowExecutionID)
	if err != nil {
		return "", err
	}
	if err := validateExec(req); err != nil {
		return "", err
	}

	timeout := m.cfg.ExecTimeout
	if req.TimeoutSeconds > 0 {
		timeout = time.Duration(req.TimeoutSeconds) * time.Second
		if timeout > 15*time.Minute {
			timeout = 15 * time.Minute
		}
	}

	ctx, cancel := context.WithTimeout(context.Background(), timeout)
	defer cancel()

	args := []string{"exec", "-w", "/workspace", sb.ContainerName, req.Command}
	args = append(args, req.Args...)
	output, err := runDocker(ctx, args...)
	m.touch(workflowExecutionID)
	if err != nil {
		return string(output), fmt.Errorf("execution failed: %w", err)
	}
	return string(output), nil
}

func validateExec(req execRequest) error {
	if req.Command == "" {
		return errBadExec
	}
	switch req.Command {
	case "npm":
		if len(req.Args) < 2 || req.Args[0] != "run" {
			return errBadExec
		}
		if _, ok := allowedNPMScript[req.Args[1]]; !ok {
			return errBadExec
		}
		return nil
	case "npx":
		if len(req.Args) < 2 || req.Args[0] != "remotion" {
			return errBadExec
		}
		switch req.Args[1] {
		case "render", "still", "compositions":
			return nil
		default:
			return errBadExec
		}
	default:
		return errBadExec
	}
}

func (m *sandboxManager) resolveSandboxPath(sb *sandbox, userPath string) (string, error) {
	clean := filepath.Clean("/" + strings.TrimSpace(userPath))
	if clean == "/" {
		clean = "."
	} else {
		clean = strings.TrimPrefix(clean, "/")
	}
	full := filepath.Clean(filepath.Join(sb.WorkspacePath, clean))
	root := filepath.Clean(sb.WorkspacePath)
	if full != root && !strings.HasPrefix(full, root+string(os.PathSeparator)) {
		return "", errInvalidPath
	}
	return full, nil
}

func (m *sandboxManager) listFiles(workflowExecutionID, relPath string) ([]fileEntry, error) {
	sb, err := m.getByExecution(workflowExecutionID)
	if err != nil {
		return nil, err
	}
	target, err := m.resolveSandboxPath(sb, relPath)
	if err != nil {
		return nil, err
	}
	entries, err := os.ReadDir(target)
	if err != nil {
		return nil, err
	}

	out := make([]fileEntry, 0, len(entries))
	for _, e := range entries {
		info, statErr := e.Info()
		if statErr != nil {
			return nil, statErr
		}
		out = append(out, fileEntry{
			Name:    e.Name(),
			IsDir:   e.IsDir(),
			Size:    info.Size(),
			ModTime: info.ModTime().UTC(),
		})
	}
	m.touch(workflowExecutionID)
	return out, nil
}

func (m *sandboxManager) readFile(workflowExecutionID, relPath string) ([]byte, error) {
	sb, err := m.getByExecution(workflowExecutionID)
	if err != nil {
		return nil, err
	}
	target, err := m.resolveSandboxPath(sb, relPath)
	if err != nil {
		return nil, err
	}
	data, err := os.ReadFile(target)
	if err != nil {
		return nil, err
	}
	m.touch(workflowExecutionID)
	return data, nil
}

func (m *sandboxManager) writeFile(workflowExecutionID, relPath string, content []byte) error {
	sb, err := m.getByExecution(workflowExecutionID)
	if err != nil {
		return err
	}
	target, err := m.resolveSandboxPath(sb, relPath)
	if err != nil {
		return err
	}
	if err := os.MkdirAll(filepath.Dir(target), 0o755); err != nil {
		return err
	}
	if err := os.WriteFile(target, content, 0o644); err != nil {
		return err
	}
	m.touch(workflowExecutionID)
	return nil
}

func (m *sandboxManager) deletePath(workflowExecutionID, relPath string) error {
	sb, err := m.getByExecution(workflowExecutionID)
	if err != nil {
		return err
	}
	target, err := m.resolveSandboxPath(sb, relPath)
	if err != nil {
		return err
	}
	if target == filepath.Clean(sb.WorkspacePath) {
		return errInvalidPath
	}
	if err := os.RemoveAll(target); err != nil {
		return err
	}
	m.touch(workflowExecutionID)
	return nil
}

func (m *sandboxManager) startJanitor(ctx context.Context) {
	ticker := time.NewTicker(time.Minute)
	go func() {
		defer ticker.Stop()
		for {
			select {
			case <-ticker.C:
				m.cleanupInactive()
			case <-ctx.Done():
				return
			}
		}
	}()
}

func (m *sandboxManager) cleanupInactive() {
	now := time.Now().UTC()
	toDelete := make([]sandbox, 0)

	m.mu.RLock()
	for _, sb := range m.sandboxes {
		if now.Sub(sb.LastActivity) > m.cfg.SandboxTTL {
			copy := *sb
			toDelete = append(toDelete, copy)
		}
	}
	m.mu.RUnlock()

	for _, sb := range toDelete {
		if err := m.deleteInternal(sb.ID, sb.WorkflowExecutionID, sb.ContainerName, sb.WorkspacePath); err != nil {
			log.Printf("janitor failed to cleanup sandbox %s: %v", sb.ID, err)
		}
	}
}

type apiHandler struct {
	manager *sandboxManager
}

type createSandboxRequest struct {
	WorkflowExecutionID string `json:"workflowExecutionId"`
}

type execRequest struct {
	Command        string   `json:"command"`
	Args           []string `json:"args"`
	TimeoutSeconds int      `json:"timeoutSeconds"`
}

type writeFileRequest struct {
	ContentBase64 string `json:"contentBase64"`
}

type installPackagesRequest struct {
	Packages []string `json:"packages"`
}

type sandboxStatus struct {
	Exists         bool      `json:"exists"`
	Ready          bool      `json:"ready"`
	HasPackageJson bool      `json:"hasPackageJson"`
	HasNodeModules bool      `json:"hasNodeModules"`
	ContainerName  string    `json:"containerName,omitempty"`
	WorkspacePath  string    `json:"workspacePath,omitempty"`
	CreatedAt      time.Time `json:"createdAt,omitempty"`
	LastActivity   time.Time `json:"lastActivity,omitempty"`
}

type fileEntry struct {
	Name    string    `json:"name"`
	IsDir   bool      `json:"isDir"`
	Size    int64     `json:"size"`
	ModTime time.Time `json:"modTime"`
}

func (h *apiHandler) createSandbox(w http.ResponseWriter, r *http.Request) {
	var req createSandboxRequest
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		writeError(w, http.StatusBadRequest, "invalid json body")
		return
	}
	sb, created, err := h.manager.create(req.WorkflowExecutionID)
	if err != nil {
		writeManagerError(w, err)
		return
	}
	status := http.StatusCreated
	if !created {
		status = http.StatusOK
	}
	writeJSON(w, status, sb)
}

func (h *apiHandler) listSandboxes(w http.ResponseWriter, r *http.Request) {
	writeJSON(w, http.StatusOK, h.manager.list())
}

func (h *apiHandler) getSandbox(w http.ResponseWriter, r *http.Request) {
	sb, err := h.manager.getByExecution(mux.Vars(r)["workflowExecutionId"])
	if err != nil {
		writeManagerError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, sb)
}

func (h *apiHandler) deleteSandbox(w http.ResponseWriter, r *http.Request) {
	if err := h.manager.deleteByExecution(mux.Vars(r)["workflowExecutionId"]); err != nil {
		writeManagerError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, map[string]bool{"ok": true})
}

func (h *apiHandler) completeWorkflow(w http.ResponseWriter, r *http.Request) {
	if err := h.manager.deleteByExecution(mux.Vars(r)["workflowExecutionId"]); err != nil {
		writeManagerError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, map[string]bool{"ok": true})
}

func (h *apiHandler) execSandbox(w http.ResponseWriter, r *http.Request) {
	var req execRequest
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		writeError(w, http.StatusBadRequest, "invalid json body")
		return
	}
	output, err := h.manager.runExec(mux.Vars(r)["workflowExecutionId"], req)
	if err != nil {
		status := http.StatusInternalServerError
		if errors.Is(err, errBadExec) || errors.Is(err, errNotFound) || errors.Is(err, errBadExecution) {
			status = statusFromErr(err)
		}
		writeJSON(w, status, map[string]string{
			"error":  err.Error(),
			"output": output,
		})
		return
	}
	writeJSON(w, http.StatusOK, map[string]string{"output": output})
}

func (h *apiHandler) listFiles(w http.ResponseWriter, r *http.Request) {
	path := r.URL.Query().Get("path")
	entries, err := h.manager.listFiles(mux.Vars(r)["workflowExecutionId"], path)
	if err != nil {
		writeManagerError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, entries)
}

func (h *apiHandler) getFileContent(w http.ResponseWriter, r *http.Request) {
	path := r.URL.Query().Get("path")
	if path == "" {
		writeError(w, http.StatusBadRequest, "path is required")
		return
	}
	data, err := h.manager.readFile(mux.Vars(r)["workflowExecutionId"], path)
	if err != nil {
		writeManagerError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, map[string]string{
		"path":          path,
		"contentBase64": base64.StdEncoding.EncodeToString(data),
	})
}

func (h *apiHandler) putFileContent(w http.ResponseWriter, r *http.Request) {
	path := r.URL.Query().Get("path")
	if path == "" {
		writeError(w, http.StatusBadRequest, "path is required")
		return
	}
	var req writeFileRequest
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		writeError(w, http.StatusBadRequest, "invalid json body")
		return
	}
	content, err := base64.StdEncoding.DecodeString(req.ContentBase64)
	if err != nil {
		writeError(w, http.StatusBadRequest, "contentBase64 must be valid base64")
		return
	}
	if err := h.manager.writeFile(mux.Vars(r)["workflowExecutionId"], path, content); err != nil {
		writeManagerError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, map[string]bool{"ok": true})
}

func (h *apiHandler) deleteFilePath(w http.ResponseWriter, r *http.Request) {
	path := r.URL.Query().Get("path")
	if path == "" {
		writeError(w, http.StatusBadRequest, "path is required")
		return
	}
	if err := h.manager.deletePath(mux.Vars(r)["workflowExecutionId"], path); err != nil {
		writeManagerError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, map[string]bool{"ok": true})
}

func (h *apiHandler) getSandboxStatus(w http.ResponseWriter, r *http.Request) {
	workflowExecutionID := mux.Vars(r)["workflowExecutionId"]
	sb, err := h.manager.getByExecution(workflowExecutionID)
	if err != nil {
		if errors.Is(err, errNotFound) {
			writeJSON(w, http.StatusOK, sandboxStatus{Exists: false})
			return
		}
		writeManagerError(w, err)
		return
	}

	hasPackageJson := fileExists(filepath.Join(sb.WorkspacePath, "package.json"))
	hasNodeModules := dirExists(filepath.Join(sb.WorkspacePath, "node_modules"))
	ready := hasPackageJson && hasNodeModules

	writeJSON(w, http.StatusOK, sandboxStatus{
		Exists:         true,
		Ready:          ready,
		HasPackageJson: hasPackageJson,
		HasNodeModules: hasNodeModules,
		ContainerName:  sb.ContainerName,
		WorkspacePath:  sb.WorkspacePath,
		CreatedAt:      sb.CreatedAt,
		LastActivity:   sb.LastActivity,
	})
}

func (h *apiHandler) installPackages(w http.ResponseWriter, r *http.Request) {
	var req installPackagesRequest
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		writeError(w, http.StatusBadRequest, "invalid json body")
		return
	}
	if len(req.Packages) == 0 {
		writeError(w, http.StatusBadRequest, "packages list is empty")
		return
	}
	for _, pkg := range req.Packages {
		if !npmPackageNameRe.MatchString(pkg) {
			writeError(w, http.StatusBadRequest, fmt.Sprintf("invalid package name: %q", pkg))
			return
		}
	}

	workflowExecutionID := mux.Vars(r)["workflowExecutionId"]
	sb, err := h.manager.getByExecution(workflowExecutionID)
	if err != nil {
		writeManagerError(w, err)
		return
	}

	timeout := h.manager.cfg.ExecTimeout
	ctx, cancel := context.WithTimeout(context.Background(), timeout)
	defer cancel()

	args := append([]string{"exec", "-w", "/workspace", sb.ContainerName, "npm", "install", "--save"}, req.Packages...)
	output, execErr := runDocker(ctx, args...)
	h.manager.touch(workflowExecutionID)
	if execErr != nil {
		writeJSON(w, http.StatusInternalServerError, map[string]string{
			"error":  execErr.Error(),
			"output": string(output),
		})
		return
	}
	writeJSON(w, http.StatusOK, map[string]string{"output": string(output)})
}

func fileExists(path string) bool {
	info, err := os.Stat(path)
	return err == nil && !info.IsDir()
}

func dirExists(path string) bool {
	info, err := os.Stat(path)
	return err == nil && info.IsDir()
}

func writeManagerError(w http.ResponseWriter, err error) {
	writeError(w, statusFromErr(err), err.Error())
}

func statusFromErr(err error) int {
	switch {
	case errors.Is(err, errNotFound):
		return http.StatusNotFound
	case errors.Is(err, errInvalidPath), errors.Is(err, errBadExec), errors.Is(err, errBadExecution):
		return http.StatusBadRequest
	default:
		return http.StatusInternalServerError
	}
}

func writeJSON(w http.ResponseWriter, status int, body interface{}) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(body)
}

func writeError(w http.ResponseWriter, status int, message string) {
	writeJSON(w, status, map[string]string{"error": message})
}

func runDocker(ctx context.Context, args ...string) ([]byte, error) {
	cmd := exec.CommandContext(ctx, "docker", args...)
	return cmd.CombinedOutput()
}

func main() {
	cfg := loadConfig()
	if err := os.MkdirAll(cfg.SandboxRoot, 0o755); err != nil {
		log.Fatalf("failed to create sandbox root: %v", err)
	}

	manager := newSandboxManager(cfg)
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	manager.startJanitor(ctx)

	handler := &apiHandler{manager: manager}
	router := mux.NewRouter()
	router.HandleFunc("/health", func(w http.ResponseWriter, r *http.Request) {
		writeJSON(w, http.StatusOK, map[string]string{"status": "ok"})
	}).Methods(http.MethodGet)

	api := router.PathPrefix("/api/v1/sandboxes").Subrouter()
	api.HandleFunc("", handler.createSandbox).Methods(http.MethodPost)
	api.HandleFunc("", handler.listSandboxes).Methods(http.MethodGet)
	api.HandleFunc("/{workflowExecutionId}", handler.getSandbox).Methods(http.MethodGet)
	api.HandleFunc("/{workflowExecutionId}", handler.deleteSandbox).Methods(http.MethodDelete)
	api.HandleFunc("/{workflowExecutionId}/status", handler.getSandboxStatus).Methods(http.MethodGet)
	api.HandleFunc("/{workflowExecutionId}/complete", handler.completeWorkflow).Methods(http.MethodPost)
	api.HandleFunc("/{workflowExecutionId}/exec", handler.execSandbox).Methods(http.MethodPost)
	api.HandleFunc("/{workflowExecutionId}/packages", handler.installPackages).Methods(http.MethodPost)
	api.HandleFunc("/{workflowExecutionId}/files", handler.listFiles).Methods(http.MethodGet)
	api.HandleFunc("/{workflowExecutionId}/files", handler.deleteFilePath).Methods(http.MethodDelete)
	api.HandleFunc("/{workflowExecutionId}/files/content", handler.getFileContent).Methods(http.MethodGet)
	api.HandleFunc("/{workflowExecutionId}/files/content", handler.putFileContent).Methods(http.MethodPut)

	addr := ":" + cfg.Port
	log.Printf("ReelForge Sandbox Executor %s listening on %s", Version, addr)
	log.Fatal(http.ListenAndServe(addr, router))
}
