package main

import (
	"context"
	"crypto/subtle"
	"encoding/base64"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"io/fs"
	"log"
	"net/http"
	"os"
	"os/exec"
	"path"
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
	Port           string
	SandboxImage   string
	SandboxRoot    string
	SandboxNetwork string // docker network used for sandbox containers
	// NetworkEgress reports whether sandbox containers are allowed to reach
	// anything outside their own network. When false (the default) the docker
	// network is created with --internal, which removes the container's default
	// route entirely: no internet, no other docker network, and — critically —
	// no route to the host gateway, which is what otherwise exposes every
	// host-published port (Postgres, RabbitMQ, MinIO, nginx) to code running
	// inside the sandbox. Enabling egress trades that guarantee for the ability
	// to npm-install at runtime; see docs/sandbox-service.md.
	NetworkEgress bool
	// APIToken is the shared secret every API caller must present. There is no
	// default and an empty value is fatal at startup: this service can start
	// containers and read and write files, so it must never run open.
	APIToken     string
	MaxSandboxes int
	SandboxTTL   time.Duration
	ExecTimeout  time.Duration
	MemoryLimit  string
	CPULimit     string
	PIDsLimit    int
}

func loadConfig() appConfig {
	return appConfig{
		Port:           getEnv("PORT", "8080"),
		SandboxImage:   getEnv("SANDBOX_IMAGE", "reelforge-sandbox-runtime:local"),
		SandboxRoot:    getEnv("SANDBOX_ROOT", "/var/lib/reelforge/sandboxes"),
		SandboxNetwork: getEnv("SANDBOX_NETWORK", "sandbox-net"),
		NetworkEgress:  getBoolEnv("SANDBOX_NETWORK_EGRESS", false),
		APIToken:       os.Getenv("SANDBOX_API_TOKEN"),
		MaxSandboxes:   getIntEnv("MAX_SANDBOXES", 20),
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

func getBoolEnv(key string, fallback bool) bool {
	v := strings.TrimSpace(os.Getenv(key))
	if v == "" {
		return fallback
	}
	b, err := strconv.ParseBool(v)
	if err != nil {
		return fallback
	}
	return b
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
	maxSandboxes   int
}

// sandboxUID/sandboxGID are the numeric ids of the `node` user in the sandbox
// runtime image, which is what each container runs as (`--user node`). The
// workspace is chowned to them so the container can write it without the
// directory being world-writable on the host.
const (
	sandboxUID = 1000
	sandboxGID = 1000
)

var (
	errNotFound       = errors.New("sandbox not found")
	errInvalidPath    = errors.New("invalid path")
	errBadExec        = errors.New("command not allowed")
	errBadExecution   = errors.New("invalid workflowExecutionId")
	errTooManySandbox = errors.New("sandbox limit reached")
	allowedNPMScript  = map[string]struct{}{
		"build":        {},
		"render":       {},
		"typecheck":    {},
		"compositions": {},
		"lint":         {},
	}
	// npmPackageNameRe matches valid npm package names including scoped packages.
	// The first character of an unscoped name is deliberately restricted to
	// [a-z0-9]: the previous pattern allowed the whole name to come from
	// [a-z0-9\-_.]+, which matches a leading dash, so a "package" of "--foo" passed
	// validation and was spliced into `npm install --save …` as a flag rather than
	// a package. Names may also not start with "." for the same reason.
	npmPackageNameRe = regexp.MustCompile(`^(@[a-z0-9][a-z0-9\-_.]*/)?[a-z0-9][a-z0-9\-_.]*(@[a-zA-Z0-9.\-_]+)?$`)
)

func newSandboxManager(cfg appConfig) *sandboxManager {
	return &sandboxManager{
		cfg:            cfg,
		maxSandboxes:   cfg.MaxSandboxes,
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
	// Check if already exists
	if existingID, ok := m.executionIndex[workflowExecutionID]; ok {
		if existing, found := m.sandboxes[existingID]; found {
			existing.LastActivity = time.Now().UTC()
			copy := *existing
			m.mu.Unlock()
			return &copy, false, nil
		}
	}

	// Check if we're at max capacity
	if len(m.sandboxes) >= m.maxSandboxes {
		m.mu.Unlock()
		return nil, false, errTooManySandbox
	}

	// Generate ID and workspace path while still holding the lock
	id := uuid.NewString()
	workspace := filepath.Join(m.cfg.SandboxRoot, id)

	// Release the lock before slow operations (container creation, directory setup)
	m.mu.Unlock()

	// 0770, not 0777: the sandbox container writes here as uid 1000 (node) and
	// this service reads it back, but nothing else on the host has any business
	// touching an in-flight workspace. The group bit is what the container needs;
	// world-writable was never required.
	if err := os.MkdirAll(workspace, 0o770); err != nil {
		return nil, false, err
	}
	if err := os.Chown(workspace, sandboxUID, sandboxGID); err != nil {
		log.Printf("warning: could not chown workspace %s to %d:%d: %v", workspace, sandboxUID, sandboxGID, err)
	}

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
		"-v", workspace+":/workspace",
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

	// Re-acquire lock to register the sandbox
	m.mu.Lock()

	// Double-check that we're still below capacity and that the execution ID wasn't claimed
	// while we were creating the container
	if len(m.sandboxes) >= m.maxSandboxes {
		m.mu.Unlock()
		log.Printf("sandbox capacity exceeded while creating container for workflow %s, cleaning up", workflowExecutionID)
		_, _ = runDocker(context.Background(), "rm", "-f", containerName)
		_ = os.RemoveAll(workspace)
		return nil, false, errTooManySandbox
	}

	if existingID, ok := m.executionIndex[workflowExecutionID]; ok {
		// Another goroutine created the sandbox for this execution while we were building
		if existing, found := m.sandboxes[existingID]; found {
			existing.LastActivity = time.Now().UTC()
			copy := *existing
			m.mu.Unlock()
			log.Printf("sandbox already exists for workflow %s, cleaning up duplicate container %s", workflowExecutionID, containerName)
			_, _ = runDocker(context.Background(), "rm", "-f", containerName)
			_ = os.RemoveAll(workspace)
			return &copy, false, nil
		}
	}

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
	// Only delete the execution index entry if it still points to this sandbox ID.
	// This prevents a race condition where another goroutine created a new container
	// for the same execution ID after this one was created but before cleanup completed.
	if m.executionIndex[workflowExecutionID] == id {
		delete(m.executionIndex, workflowExecutionID)
	}
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

// maxReadFileBytes caps a single /files/content read. The workspace is written
// by code running inside the sandbox, so its file sizes are attacker-chosen and
// the whole body is buffered in memory and base64-encoded before it is sent.
const maxReadFileBytes = 64 << 20 // 64 MiB

// normalizeSandboxPath turns a caller-supplied path into a path relative to the
// workspace root. Lexical cleaning here only neutralizes the easy cases; real
// containment is enforced by os.Root in openWorkspace, which is what makes
// symlinks safe.
func normalizeSandboxPath(userPath string) string {
	clean := path.Clean("/" + strings.TrimSpace(strings.ReplaceAll(userPath, `\`, "/")))
	rel := strings.TrimPrefix(clean, "/")
	if rel == "" {
		return "."
	}
	return rel
}

// openWorkspace opens the sandbox workspace as an os.Root. Every file operation
// this service performs must go through the returned root.
//
// This is a security boundary, not a convenience. The previous implementation
// resolved paths lexically (filepath.Clean + a HasPrefix check), which cannot
// see symlinks: code running inside the sandbox container — which is fully
// attacker-controlled, see validateExec — could create /workspace/esc -> / and
// this service would then follow it while reading, writing, or deleting. Those
// operations run in *this* container's mount namespace, which has the Docker
// socket bind-mounted, so a write through such a link escalates from "arbitrary
// code in the sandbox" to "arbitrary code in the control plane" and from there
// to root on the host. os.Root refuses any traversal that leaves the root,
// including via symlink, and re-checks on every path component.
func openWorkspace(sb *sandbox) (*os.Root, error) {
	return os.OpenRoot(sb.WorkspacePath)
}

// mkdirAllIn is os.MkdirAll confined to root. os.Root gained MkdirAll in Go
// 1.25; this walks the components by hand so the service still builds on 1.24.
func mkdirAllIn(root *os.Root, dir string) error {
	if dir == "." || dir == "" {
		return nil
	}
	current := ""
	for _, part := range strings.Split(dir, "/") {
		if part == "" || part == "." {
			continue
		}
		if current == "" {
			current = part
		} else {
			current += "/" + part
		}
		if err := root.Mkdir(current, 0o755); err != nil && !errors.Is(err, fs.ErrExist) {
			return err
		}
	}
	return nil
}

// removeAllIn is os.RemoveAll confined to root (os.Root gained RemoveAll in Go
// 1.25). It uses Lstat so a symlink is unlinked rather than followed — deleting
// a link must never delete what it points at.
func removeAllIn(root *os.Root, rel string) error {
	info, err := root.Lstat(rel)
	if err != nil {
		if errors.Is(err, fs.ErrNotExist) {
			return nil
		}
		return err
	}
	if info.IsDir() {
		dir, err := root.Open(rel)
		if err != nil {
			return err
		}
		names, readErr := dir.Readdirnames(-1)
		_ = dir.Close()
		if readErr != nil {
			return readErr
		}
		for _, name := range names {
			if err := removeAllIn(root, path.Join(rel, name)); err != nil {
				return err
			}
		}
	}
	return root.Remove(rel)
}

// asPathError maps an os.Root containment refusal onto errInvalidPath so the
// caller answers 400 rather than leaking the underlying filesystem error.
func asPathError(err error) error {
	if err == nil {
		return nil
	}
	if errors.Is(err, fs.ErrNotExist) {
		return err
	}
	return fmt.Errorf("%w: %w", errInvalidPath, err)
}

func (m *sandboxManager) listFiles(workflowExecutionID, relPath string) ([]fileEntry, error) {
	sb, err := m.getByExecution(workflowExecutionID)
	if err != nil {
		return nil, err
	}
	root, err := openWorkspace(sb)
	if err != nil {
		return nil, err
	}
	defer root.Close()

	dir, err := root.Open(normalizeSandboxPath(relPath))
	if err != nil {
		return nil, asPathError(err)
	}
	defer dir.Close()

	entries, err := dir.ReadDir(-1)
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
	root, err := openWorkspace(sb)
	if err != nil {
		return nil, err
	}
	defer root.Close()

	f, err := root.Open(normalizeSandboxPath(relPath))
	if err != nil {
		return nil, asPathError(err)
	}
	defer f.Close()

	// Refuse anything that is not a regular file: opening a device or FIFO the
	// sandbox planted in its workspace would otherwise block or stream forever.
	info, err := f.Stat()
	if err != nil {
		return nil, err
	}
	if !info.Mode().IsRegular() {
		return nil, errInvalidPath
	}

	data, err := io.ReadAll(io.LimitReader(f, maxReadFileBytes))
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
	rel := normalizeSandboxPath(relPath)
	if rel == "." {
		return errInvalidPath
	}

	root, err := openWorkspace(sb)
	if err != nil {
		return err
	}
	defer root.Close()

	if err := mkdirAllIn(root, path.Dir(rel)); err != nil {
		return asPathError(err)
	}
	f, err := root.OpenFile(rel, os.O_WRONLY|os.O_CREATE|os.O_TRUNC, 0o644)
	if err != nil {
		return asPathError(err)
	}
	if _, err := f.Write(content); err != nil {
		_ = f.Close()
		return err
	}
	if err := f.Close(); err != nil {
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
	rel := normalizeSandboxPath(relPath)
	if rel == "." {
		return errInvalidPath
	}

	root, err := openWorkspace(sb)
	if err != nil {
		return err
	}
	defer root.Close()

	if err := removeAllIn(root, rel); err != nil {
		return asPathError(err)
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
	if !h.manager.cfg.NetworkEgress {
		// Fail with an explanation rather than letting npm spend the whole exec
		// timeout failing to resolve registry.npmjs.org on an --internal network.
		writeError(w, http.StatusConflict,
			"sandbox containers have no network egress, so packages cannot be installed at runtime. "+
				"Add the dependency to the baked-in Remotion template image instead, or set "+
				"SANDBOX_NETWORK_EGRESS=true to allow sandboxed code to reach the network.")
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

	// "--" terminates npm's own flag parsing, so even if a package name slips past
	// npmPackageNameRe it is treated as an operand and never as an option.
	args := append([]string{"exec", "-w", "/workspace", sb.ContainerName, "npm", "install", "--save", "--"}, req.Packages...)
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
	case errors.Is(err, errNotFound), errors.Is(err, fs.ErrNotExist):
		return http.StatusNotFound
	case errors.Is(err, errInvalidPath), errors.Is(err, errBadExec), errors.Is(err, errBadExecution):
		return http.StatusBadRequest
	case errors.Is(err, errTooManySandbox):
		return http.StatusTooManyRequests
	default:
		return http.StatusInternalServerError
	}
}

// maxRequestBodyBytes caps any JSON request body. writeFile bodies are base64,
// so this bounds a written file at roughly three quarters of the limit.
const maxRequestBodyBytes = 96 << 20 // 96 MiB

// requireAPIToken rejects any request that does not carry the shared secret in
// `Authorization: Bearer <token>`.
//
// This service has no notion of a user: every caller can start containers and
// read and write files under SANDBOX_ROOT, so the only access control available
// is "does the caller hold the secret". The comparison is constant-time so a
// caller cannot recover the token a byte at a time from response timing.
func requireAPIToken(token string, next http.Handler) http.Handler {
	expected := []byte(token)
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		presented := []byte(strings.TrimSpace(strings.TrimPrefix(r.Header.Get("Authorization"), "Bearer ")))
		if subtle.ConstantTimeCompare(presented, expected) != 1 {
			log.Printf("rejected unauthenticated %s %s from %s", r.Method, r.URL.Path, r.RemoteAddr)
			writeError(w, http.StatusUnauthorized, "unauthorized")
			return
		}
		r.Body = http.MaxBytesReader(w, r.Body, maxRequestBodyBytes)
		next.ServeHTTP(w, r)
	})
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

// ensureSandboxNetwork creates the docker network sandbox containers attach to,
// and verifies it has the isolation this service assumes.
//
// The network is created with --internal unless egress is explicitly enabled.
// --internal removes the container's default route, which is the only thing that
// actually stops sandboxed code from reaching the host: on a normal bridge the
// container can address the host gateway, and every port docker publishes is
// bound there, so Postgres, RabbitMQ, MinIO and nginx are all one hop away.
// Docker network segmentation alone does not prevent that.
//
// The network is also re-inspected when it already exists, because a network
// left over from an earlier release was created without --internal and would
// silently keep its egress.
func ensureSandboxNetwork(cfg appConfig) {
	args := []string{"network", "create"}
	if !cfg.NetworkEgress {
		args = append(args, "--internal")
	}
	args = append(args, cfg.SandboxNetwork)

	if out, err := runDocker(context.Background(), args...); err != nil {
		// Already exists is the normal path on restart; anything else is fatal,
		// since falling back to the default bridge would silently drop isolation.
		if !strings.Contains(string(out), "already exists") {
			log.Fatalf("failed to create sandbox network %s: %v\noutput: %s", cfg.SandboxNetwork, err, string(out))
		}
	}

	out, err := runDocker(context.Background(), "network", "inspect", cfg.SandboxNetwork, "--format", "{{.Internal}}")
	if err != nil {
		log.Fatalf("failed to inspect sandbox network %s: %v\noutput: %s", cfg.SandboxNetwork, err, string(out))
	}
	isInternal := strings.TrimSpace(string(out)) == "true"
	switch {
	case !cfg.NetworkEgress && !isInternal:
		log.Fatalf("sandbox network %s exists but is not internal, so sandboxed code could reach the host "+
			"gateway and every published port. Remove it (docker network rm %s) and restart, or set "+
			"SANDBOX_NETWORK_EGRESS=true to accept that exposure deliberately.",
			cfg.SandboxNetwork, cfg.SandboxNetwork)
	case cfg.NetworkEgress:
		log.Printf("WARNING: SANDBOX_NETWORK_EGRESS is enabled — sandbox containers can reach the network. "+
			"Ensure no service is published on a host interface the sandbox can route to; only nginx should "+
			"be published beyond loopback. (network %s, internal=%v)", cfg.SandboxNetwork, isInternal)
	default:
		log.Printf("sandbox network %s ready (internal=true, no egress)", cfg.SandboxNetwork)
	}
}

func main() {
	cfg := loadConfig()
	if cfg.APIToken == "" {
		log.Fatal("SANDBOX_API_TOKEN is required and must not be empty: this service starts containers " +
			"and reads and writes files on behalf of its callers, so it refuses to run unauthenticated. " +
			"Set it to a long random value shared with the workflow engine (Sandbox__ApiToken).")
	}
	if err := os.MkdirAll(cfg.SandboxRoot, 0o755); err != nil {
		log.Fatalf("failed to create sandbox root: %v", err)
	}
	// ensure any pre-existing containers are gone before we begin
	purgeExistingContainers(cfg)

	if cfg.SandboxNetwork != "" {
		ensureSandboxNetwork(cfg)
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
	api.Use(func(next http.Handler) http.Handler { return requireAPIToken(cfg.APIToken, next) })
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
	if err := http.ListenAndServe(addr, router); err != nil {
		// Unwind the deferred cancel() ourselves: log.Fatal calls os.Exit,
		// which skips defers.
		cancel()
		log.Fatal(err) //nolint:gocritic // exitAfterDefer: cancel() is invoked explicitly above
	}
}
