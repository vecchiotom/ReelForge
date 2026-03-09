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
	SandboxNetwork string   // docker network used for sandbox containers
	SandboxTTL   time.Duration
	ExecTimeout  time.Duration
	MemoryLimit  string
	CPULimit     string
	PIDsLimit    int
}

func loadConfig() appConfig {
	return appConfig{
		Port:         getEnv("PORT", "8080"),
		SandboxImage:   getEnv("SANDBOX_IMAGE", "reelforge-sandbox-executor:local"),
		SandboxRoot:    getEnv("SANDBOX_ROOT", "/var/lib/reelforge/sandboxes"),
		SandboxNetwork: getEnv("SANDBOX_NETWORK", "sandbox-net"),
		SandboxTTL:     getDurationEnv("SANDBOX_TTL", time.Minute),
		ExecTimeout:    getDurationEnv("SANDBOX_EXEC_TIMEOUT", 5*time.Minute),
		MemoryLimit:    getEnv("SANDBOX_MEMORY_LIMIT", "2g"),
		CPULimit:       getEnv("SANDBOX_CPU_LIMIT", "2"),
		PIDsLimit:      getIntEnv("SANDBOX_PIDS_LIMIT", 256),
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
	initCommand := `if [ ! -f /workspace/package.json ]; then \
		cp -r /opt/remotion-template/. /workspace/; \
		# make sure the path for the headless-shell binary exists. if Remotion has
		# already downloaded the real chrome-headless-shell binary during npm
		# install, leave it alone; otherwise fall back to symlinking the system
		# Chromium binary so that any stray spawn attempts succeed.
		mkdir -p /workspace/node_modules/.remotion/chrome-headless-shell/linux64/chrome-headless-shell-linux64/; \
		if [ ! -x /workspace/node_modules/.remotion/chrome-headless-shell/linux64/chrome-headless-shell-linux64/chrome-headless-shell ]; then \
			ln -sf /usr/bin/chromium \
				/workspace/node_modules/.remotion/chrome-headless-shell/linux64/chrome-headless-shell-linux64/chrome-headless-shell || true; \
		fi; \
	fi; sleep infinity`
	args := []string{"run", "-d", "--name", containerName}
	if m.cfg.SandboxNetwork != "" {
		args = append(args, "--network", m.cfg.SandboxNetwork)
	}
	args = append(args,
		"--read-only",
		"--tmpfs", "/tmp:rw,nosuid,nodev,size=256m",
		"--tmpfs", "/home/node/.npm:rw,nosuid,nodev,size=512m",
		"--memory", m.cfg.MemoryLimit,
		"--cpus", m.cfg.CPULimit,
		"--pids-limit", strconv.Itoa(m.cfg.PIDsLimit),
		"--security-opt", "no-new-privileges",
		"--cap-drop", "ALL",
		"--user", "node",
		"-v", workspace + ":/workspace",
		"-w", "/workspace",
		"--entrypoint", "sh",
		m.cfg.SandboxImage,
		"-c", initCommand,
	)
	if out, err := runDocker(context.Background(), args...); err != nil {
		log.Printf("failed to start sandbox container, output: %s, err: %v", string(out), err)
		_ = os.RemoveAll(workspace)
		return nil, false, fmt.Errorf("failed to start sandbox container: %w\noutput: %s", err, string(out))
	} else {
		log.Printf("sandbox container %s created for workflow %s", containerName, workflowExecutionID)
	}

	// Wait for the container's init script to finish copying the Remotion template
	// (cp -a runs asynchronously inside the detached container).
	log.Printf("waiting for template init in sandbox %s (workflow %s)", containerName, workflowExecutionID)
	pkgJsonPath := filepath.Join(workspace, "package.json")
	initDeadline := time.Now().Add(60 * time.Second)
	for time.Now().Before(initDeadline) {
		if fileExists(pkgJsonPath) {
			break
		}
		time.Sleep(250 * time.Millisecond)
	}
	if !fileExists(pkgJsonPath) {
		log.Printf("sandbox init timed out for container %s, cleaning up", containerName)
		_, _ = runDocker(context.Background(), "rm", "-f", containerName)
		_ = os.RemoveAll(workspace)
		return nil, false, fmt.Errorf("sandbox init timed out: package.json not found after 60s")
	}
	log.Printf("sandbox %s ready (workflow %s)", containerName, workflowExecutionID)

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
	log.Printf("deleting sandbox container %s and workspace %s", containerName, workspace)
	if out, err := runDocker(context.Background(), "rm", "-f", containerName); err != nil {
		log.Printf("error deleting container %s: %v, output: %s", containerName, err, string(out))
	}
	if err := os.RemoveAll(workspace); err != nil {
		log.Printf("error removing workspace %s: %v", workspace, err)
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

	// make sure the workspace has a valid headless-shell link before any
	// commands run; this is cheap and idempotent.
	_ = ensureHeadlessLink(sb.ContainerName)

	// normalize request args (in-place) before executing
	sanitizeExecRequest(&req)

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
	log.Printf("exec request for workflow %s: %s %v", workflowExecutionID, req.Command, req.Args)
	output, err := runDocker(ctx, args...)
	m.touch(workflowExecutionID)
	if err != nil {
		log.Printf("exec failed for workflow %s: %v; output: %s", workflowExecutionID, err, string(output))
		return string(output), fmt.Errorf("execution failed: %w", err)
	}
	log.Printf("exec succeeded for workflow %s; output length %d", workflowExecutionID, len(output))
	return string(output), nil
}

// sanitizeExecRequest ensures that certain dangerous or missing arguments are
// added or normalized before a container exec is performed. Remotion projects
// require a Chromium-like binary capable of running in headless mode. The
// upstream Chrome binary no longer includes the traditional `--headless` flag
// (error seen: "headless mode has been replaced and is no longer included in
// the chrome binary"), so we explicitly point the CLI at the lightweight
// headless-shell binary bundled by Remotion. The sandbox image still ships a
// full `/usr/bin/chromium` for convenience, but the headless-shell is the
// correct target when rendering videos.
func sanitizeExecRequest(req *execRequest) {
	if req == nil {
		return
	}
	if req.Command == "npx" && len(req.Args) >= 2 && req.Args[0] == "remotion" &&
		req.Args[1] == "render" {
		has := false
		for _, a := range req.Args[2:] {
			if strings.HasPrefix(a, "--chromium-executable") {
				has = true
				break
			}
		}
		if !has {
			// point at the path where ensureHeadlessLink creates a symlink; the
			// container entrypoint also ensures the directory exists so this should
			// always work.
			req.Args = append(req.Args, "--chromium-executable=/workspace/node_modules/.remotion/chrome-headless-shell/linux64/chrome-headless-shell-linux64/chrome-headless-shell")
		}
	}
}

// ensureHeadlessLink creates a symlink inside the sandbox container pointing at
// the system Chromium binary. Calling it repeatedly is safe.
func ensureHeadlessLink(container string) error {
	// Prefer an existing headless-shell binary; only create a fallback symlink to
	// system Chromium if the file is missing or not executable. This lets us run
	// the proper headless build when available while still avoiding ENOENT for
	// older remotion versions or stripped image builds.
	cmd := `mkdir -p /workspace/node_modules/.remotion/chrome-headless-shell/linux64/chrome-headless-shell-linux64/ && ` +
		`if [ ! -x /workspace/node_modules/.remotion/chrome-headless-shell/linux64/chrome-headless-shell-linux64/chrome-headless-shell ]; then ` +
		`ln -sf /usr/bin/chromium /workspace/node_modules/.remotion/chrome-headless-shell/linux64/chrome-headless-shell-linux64/chrome-headless-shell ; fi || true`
	_, err := runDocker(context.Background(), "exec", container, "sh", "-c", cmd)
	return err
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
	// run more frequently than the TTL so containers are removed within a
	// minute even when the TTL is small. default tick interval is 30s.
	interval := 30 * time.Second
	if m.cfg.SandboxTTL > 0 && m.cfg.SandboxTTL < interval {
		interval = m.cfg.SandboxTTL / 2
		if interval < 10*time.Second {
			interval = 10 * time.Second
		}
	}
	ticker := time.NewTicker(interval)
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
		log.Printf("janitor cleaning up sandbox %s (workflow %s)", sb.ID, sb.WorkflowExecutionID)
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
	log.Printf("API create sandbox request for workflow %s", req.WorkflowExecutionID)
	sb, created, err := h.manager.create(req.WorkflowExecutionID)
	if err != nil {
		log.Printf("error creating sandbox for workflow %s: %v", req.WorkflowExecutionID, err)
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
	log.Printf("API list sandboxes request")
	writeJSON(w, http.StatusOK, h.manager.list())
}

func (h *apiHandler) getSandbox(w http.ResponseWriter, r *http.Request) {
	workflowExecutionID := mux.Vars(r)["workflowExecutionId"]
	log.Printf("API get sandbox request for workflow %s", workflowExecutionID)
	sb, err := h.manager.getByExecution(workflowExecutionID)
	if err != nil {
		writeManagerError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, sb)
}

func (h *apiHandler) deleteSandbox(w http.ResponseWriter, r *http.Request) {
	workflowExecutionID := mux.Vars(r)["workflowExecutionId"]
	log.Printf("API delete sandbox request for workflow %s", workflowExecutionID)
	if err := h.manager.deleteByExecution(workflowExecutionID); err != nil {
		log.Printf("API delete sandbox error for workflow %s: %v", workflowExecutionID, err)
		writeManagerError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, map[string]bool{"ok": true})
}

func (h *apiHandler) completeWorkflow(w http.ResponseWriter, r *http.Request) {
	workflowExecutionID := mux.Vars(r)["workflowExecutionId"]
	log.Printf("API complete workflow (delete sandbox) request for workflow %s", workflowExecutionID)
	if err := h.manager.deleteByExecution(workflowExecutionID); err != nil {
		log.Printf("API complete workflow error for workflow %s: %v", workflowExecutionID, err)
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
	// treating a status check as activity so polls won't trigger the janitor
	h.manager.touch(workflowExecutionID)

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
		log.Printf("install packages error for workflow %s: %v output: %s", workflowExecutionID, execErr, string(output))
		writeJSON(w, http.StatusInternalServerError, map[string]string{
			"error":  execErr.Error(),
			"output": string(output),
		})
		return
	}
	log.Printf("installed packages %v for workflow %s", req.Packages, workflowExecutionID)
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

// dockerRunner is the signature used for executing docker commands. It can be
// overridden by tests to avoid invoking the real docker binary.
type dockerRunner func(ctx context.Context, args ...string) ([]byte, error)

// runDocker is the canonical implementation used by production code. Tests may
// replace this variable with a stub.
var runDocker dockerRunner = runDockerImpl

func runDockerImpl(ctx context.Context, args ...string) ([]byte, error) {
	// log the docker command being executed for debugging purposes
	log.Printf("[docker] running: docker %s", strings.Join(args, " "))
	cmd := exec.CommandContext(ctx, "docker", args...)
	output, err := cmd.CombinedOutput()
	if len(output) > 0 {
		// log output even on success so users can diagnose state
		log.Printf("[docker] output: %s", string(output))
	}
	if err != nil {
		log.Printf("[docker] error: %v", err)
	}
	return output, err
}

// purgeExistingContainers inspects Docker for any lingering sandbox
// containers from previous runs (name prefix rf-sbx-) and removes them along
// with their workspace directories.  This ensures a clean state on service
// startup; the in‑memory manager has no knowledge of containers created before
// it started, so without this step they would hang around forever.
func purgeExistingContainers(cfg appConfig) {
	out, err := runDocker(context.Background(), "ps", "-a", "--filter", "name=rf-sbx-", "--format", "{{.Names}}")
	if err != nil {
		log.Printf("warning: could not list existing sandbox containers: %v", err)
		return
	}
	for _, name := range strings.Fields(string(out)) {
		if name == "" {
			continue
		}
		log.Printf("purging leftover sandbox container %s", name)
		_, _ = runDocker(context.Background(), "rm", "-f", name)
		// container names are rf-sbx-<id>
		id := strings.TrimPrefix(name, "rf-sbx-")
		if id != name {
			dir := filepath.Join(cfg.SandboxRoot, id)
			if err := os.RemoveAll(dir); err != nil {
				log.Printf("error removing leftover workspace %s: %v", dir, err)
			}
		}
	}
	// wipe any orphaned workspace folders that don't correspond to a container
	if entries, err := os.ReadDir(cfg.SandboxRoot); err == nil {
		for _, e := range entries {
			if e.IsDir() {
				_ = os.RemoveAll(filepath.Join(cfg.SandboxRoot, e.Name()))
			}
		}
	}
}

func main() {
	cfg := loadConfig()
	if err := os.MkdirAll(cfg.SandboxRoot, 0o755); err != nil {
		log.Fatalf("failed to create sandbox root: %v", err)
	}
	// ensure any pre-existing containers are gone before we begin
	purgeExistingContainers(cfg)

	// ensure sandbox network exists so that containers can reach npm registry
	// while remaining isolated from the primary application network
	if cfg.SandboxNetwork != "" {
		// create network if missing (ignore errors if it already exists)
		_, _ = runDocker(context.Background(), "network", "inspect", cfg.SandboxNetwork)
		_, _ = runDocker(context.Background(), "network", "create", cfg.SandboxNetwork)
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
