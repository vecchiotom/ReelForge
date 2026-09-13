package main

import (
	"context"
	"errors"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"reflect"
	"strings"
	"testing"
)

func TestValidateExec(t *testing.T) {
	good := []execRequest{
		{Command: "npm", Args: []string{"run", "build"}},
		{Command: "npm", Args: []string{"run", "render"}},
		{Command: "npm", Args: []string{"run", "typecheck"}},
		{Command: "npx", Args: []string{"remotion", "render", "Main"}},
		{Command: "npx", Args: []string{"remotion", "still", "Main"}},
		{Command: "npx", Args: []string{"remotion", "compositions"}},
	}
	for _, req := range good {
		if err := validateExec(req); err != nil {
			t.Errorf("validateExec rejected valid request %v: %v", req, err)
		}
	}

	bad := []execRequest{
		{Command: "", Args: []string{}},
		{Command: "npm", Args: []string{"install"}},
		{Command: "npm", Args: []string{"run"}},
		{Command: "npm", Args: []string{"run", "notallowed"}},
		{Command: "npx", Args: []string{"node", "script.js"}},
		{Command: "npx", Args: []string{"remotion"}},
		{Command: "npx", Args: []string{"remotion", "unknown"}},
		{Command: "docker", Args: []string{"run"}},
	}
	for _, req := range bad {
		if err := validateExec(req); err == nil {
			t.Errorf("validateExec accepted invalid request %v", req)
		}
	}
}

func TestSanitizeExecRequest(t *testing.T) {
	// should append flag when absent and use headless-shell path
	req := execRequest{Command: "npx", Args: []string{"remotion", "render", "Comp"}}
	sanitizeExecRequest(&req)
	wantEnds := "--chromium-executable=/workspace/node_modules/.remotion/chrome-headless-shell/linux64/chrome-headless-shell-linux64/chrome-headless-shell"
	if req.Args[len(req.Args)-1] != wantEnds {
		t.Errorf("expected headless-shell arg appended, got %v", req.Args)
	}

	// should not duplicate if already present
	req2 := execRequest{Command: "npx", Args: []string{"remotion", "render", "C", "--chromium-executable=/workspace/node_modules/.remotion/chrome-headless-shell/linux64/chrome-headless-shell-linux64/chrome-headless-shell"}}
	sanitizeExecRequest(&req2)
	count := 0
	for _, a := range req2.Args {
		if a == wantEnds {
			count++
		}
	}
	if count != 1 {
		t.Errorf("expected exactly one chromium arg, got %d in %v", count, req2.Args)
	}

	// non-render command should not be touched
	req3 := execRequest{Command: "npm", Args: []string{"run", "build"}}
	old := req3.Args
	sanitizeExecRequest(&req3)
	if !reflect.DeepEqual(old, req3.Args) {
		t.Errorf("unexpected modification of npm request: %v vs %v", old, req3.Args)
	}
}

func TestEnsureHeadlessLink(t *testing.T) {
	var lastArgs []string
	// temporarily override runDocker
	orig := runDocker
	runDocker = func(ctx context.Context, args ...string) ([]byte, error) {
		lastArgs = args
		return []byte("ok"), nil
	}
	defer func() { runDocker = orig }()

	err := ensureHeadlessLink("container123")
	if err != nil {
		t.Fatalf("expected no error, got %v", err)
	}
	if len(lastArgs) < 5 || lastArgs[0] != "exec" || lastArgs[1] != "container123" {
		t.Errorf("unexpected docker args: %v", lastArgs)
	}
	// ensure the inner shell command contains our conditional check and path
	cmd := lastArgs[len(lastArgs)-1]
	if !strings.Contains(cmd, "[ ! -x /workspace/node_modules/.remotion/chrome-headless-shell") {
		t.Errorf("command did not include existence check: %v", cmd)
	}
	if !strings.Contains(cmd, "/workspace/node_modules/.remotion/chrome-headless-shell/linux64/chrome-headless-shell-linux64/chrome-headless-shell") {
		t.Errorf("command did not reference headless-shell path: %v", cmd)
	}
}

// newTestSandbox creates a real workspace directory on disk and returns a
// sandbox pointing at it, so the file-operation tests exercise the actual
// os.Root containment rather than a mock.
func newTestSandbox(t *testing.T) (*sandboxManager, *sandbox) {
	t.Helper()
	root := t.TempDir()
	workspace := filepath.Join(root, "ws")
	if err := os.MkdirAll(workspace, 0o770); err != nil {
		t.Fatalf("mkdir workspace: %v", err)
	}
	m := newSandboxManager(appConfig{SandboxRoot: root})
	sb := &sandbox{
		ID:                  "test-id",
		WorkflowExecutionID: "",
		ContainerName:       "rf-sbx-test",
		WorkspacePath:       workspace,
	}
	m.sandboxes[sb.ID] = sb
	return m, sb
}

// A symlink planted inside the workspace must never be followed out of it. This
// is the sandbox-to-host escalation path: the sandbox container can create the
// link, and this service resolves it inside its own mount namespace, which has
// the Docker socket mounted.
func TestWorkspaceRootRefusesSymlinkEscape(t *testing.T) {
	_, sb := newTestSandbox(t)

	outside := filepath.Join(filepath.Dir(sb.WorkspacePath), "outside.txt")
	if err := os.WriteFile(outside, []byte("host secret"), 0o600); err != nil {
		t.Fatalf("seed outside file: %v", err)
	}

	links := map[string]string{
		"abs-escape": "/",
		"rel-escape": "../",
		"direct":     outside,
	}
	for name, target := range links {
		if err := os.Symlink(target, filepath.Join(sb.WorkspacePath, name)); err != nil {
			t.Fatalf("symlink %s: %v", name, err)
		}
	}

	root, err := openWorkspace(sb)
	if err != nil {
		t.Fatalf("openWorkspace: %v", err)
	}
	defer root.Close()

	for name := range links {
		if _, err := root.Open(name + "/outside.txt"); err == nil {
			t.Errorf("reading through symlink %q escaped the workspace", name)
		}
		if _, err := root.OpenFile(name+"/planted.txt", os.O_WRONLY|os.O_CREATE, 0o644); err == nil {
			t.Errorf("writing through symlink %q escaped the workspace", name)
		}
	}

	if _, err := os.ReadFile(outside); err != nil {
		t.Fatalf("outside file should be untouched: %v", err)
	}
}

// Deleting a symlink must unlink the link, never recurse into its target.
func TestRemoveAllInDoesNotFollowSymlink(t *testing.T) {
	_, sb := newTestSandbox(t)

	victimDir := filepath.Join(filepath.Dir(sb.WorkspacePath), "victim")
	if err := os.MkdirAll(victimDir, 0o750); err != nil {
		t.Fatalf("mkdir victim: %v", err)
	}
	victimFile := filepath.Join(victimDir, "keep.txt")
	if err := os.WriteFile(victimFile, []byte("keep"), 0o600); err != nil {
		t.Fatalf("seed victim file: %v", err)
	}
	if err := os.Symlink(victimDir, filepath.Join(sb.WorkspacePath, "link")); err != nil {
		t.Fatalf("symlink: %v", err)
	}

	root, err := openWorkspace(sb)
	if err != nil {
		t.Fatalf("openWorkspace: %v", err)
	}
	defer root.Close()

	if err := removeAllIn(root, "link"); err != nil {
		t.Fatalf("removeAllIn: %v", err)
	}
	if _, err := os.Stat(victimFile); err != nil {
		t.Errorf("removeAllIn followed the symlink and deleted its target: %v", err)
	}
}

func TestNormalizeSandboxPath(t *testing.T) {
	cases := map[string]string{
		"":                 ".",
		"/":                ".",
		"src/root.tsx":     "src/root.tsx",
		"/src/root.tsx":    "src/root.tsx",
		"../../etc/passwd": "etc/passwd",
		"src/../../../etc": "etc",
		"./src//a.tsx":     "src/a.tsx",
		`..\..\etc\passwd`: "etc/passwd",
	}
	for input, want := range cases {
		if got := normalizeSandboxPath(input); got != want {
			t.Errorf("normalizeSandboxPath(%q) = %q, want %q", input, got, want)
		}
	}
}

// A package name may not begin with a dash, or it lands in `npm install --save`
// as a flag rather than as a package.
func TestNpmPackageNameRejectsFlags(t *testing.T) {
	bad := []string{"--foo", "-g", "--registry=http://evil", ".hidden", "-", "--"}
	for _, name := range bad {
		if npmPackageNameRe.MatchString(name) {
			t.Errorf("package name %q should be rejected", name)
		}
	}
	good := []string{"react", "d3@7.0.0", "@remotion/shapes", "@remotion/shapes@4.0.0", "framer-motion"}
	for _, name := range good {
		if !npmPackageNameRe.MatchString(name) {
			t.Errorf("package name %q should be accepted", name)
		}
	}
}

func TestStatusFromErr(t *testing.T) {
	cases := []struct {
		err  error
		want int
	}{
		{errNotFound, http.StatusNotFound},
		// A missing file inside the workspace is a 404, not a 500 — os.Root
		// returns *PathError wrapping ENOENT and callers pass it through.
		{&os.PathError{Op: "openat", Path: "src/gone.tsx", Err: os.ErrNotExist}, http.StatusNotFound},
		{errInvalidPath, http.StatusBadRequest},
		{errBadExec, http.StatusBadRequest},
		{errBadExecution, http.StatusBadRequest},
		{errors.New("boom"), http.StatusInternalServerError},
	}
	for _, tc := range cases {
		if got := statusFromErr(tc.err); got != tc.want {
			t.Errorf("statusFromErr(%v) = %d, want %d", tc.err, got, tc.want)
		}
	}
}

func TestRequireAPIToken(t *testing.T) {
	var reached bool
	handler := requireAPIToken("s3cret", http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		reached = true
		w.WriteHeader(http.StatusOK)
	}))

	cases := []struct {
		header string
		want   int
	}{
		{"", http.StatusUnauthorized},
		{"Bearer wrong", http.StatusUnauthorized},
		{"s3cretx", http.StatusUnauthorized},
		{"Bearer s3cret", http.StatusOK},
		{"s3cret", http.StatusOK},
	}
	for _, tc := range cases {
		reached = false
		req := httptest.NewRequest(http.MethodGet, "/api/v1/sandboxes", nil)
		if tc.header != "" {
			req.Header.Set("Authorization", tc.header)
		}
		rec := httptest.NewRecorder()
		handler.ServeHTTP(rec, req)
		if rec.Code != tc.want {
			t.Errorf("Authorization %q: got %d, want %d", tc.header, rec.Code, tc.want)
		}
		if reached != (tc.want == http.StatusOK) {
			t.Errorf("Authorization %q: handler reached = %v", tc.header, reached)
		}
	}
}

// The sandbox network must be created with --internal unless egress is
// explicitly opted into, since that flag is what removes the route to the host.
func TestEnsureSandboxNetworkRequestsInternal(t *testing.T) {
	var createArgs []string
	orig := runDocker
	runDocker = func(ctx context.Context, args ...string) ([]byte, error) {
		if len(args) > 1 && args[1] == "create" {
			createArgs = args
		}
		if len(args) > 1 && args[1] == "inspect" {
			return []byte("true\n"), nil
		}
		return []byte(""), nil
	}
	defer func() { runDocker = orig }()

	ensureSandboxNetwork(appConfig{SandboxNetwork: "sandbox-net", NetworkEgress: false})

	var hasInternal bool
	for _, a := range createArgs {
		if a == "--internal" {
			hasInternal = true
		}
	}
	if !hasInternal {
		t.Errorf("network was not created with --internal: %v", createArgs)
	}
}
