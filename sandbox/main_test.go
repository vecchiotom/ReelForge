package main

import (
    "reflect"
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
    // should append flag when absent
    req := execRequest{Command: "npx", Args: []string{"remotion", "render", "Comp"}}
    sanitizeExecRequest(&req)
    wantEnds := "--chromium-executable=/usr/bin/chromium"
    if req.Args[len(req.Args)-1] != wantEnds {
        t.Errorf("expected chromium arg appended, got %v", req.Args)
    }

    // should not duplicate if already present
    req2 := execRequest{Command: "npx", Args: []string{"remotion", "render", "C", "--chromium-executable=/usr/bin/chromium"}}
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
    // ensure the inner shell command contains the symlink path and Chromium path
    found := strings.Contains(lastArgs[len(lastArgs)-1], "/workspace/node_modules/.remotion/chrome-headless-shell")
    if !found {
        t.Errorf("command did not include expected symlink creation: %v", lastArgs)
    }
}
