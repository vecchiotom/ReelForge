# Sandbox Service

The sandbox service (`/sandbox`) is a lightweight Go microservice that provides isolated, ephemeral execution environments for Remotion video composition projects. Each environment is backed by a Docker container and a host-mounted workspace directory. The service lifecycle is tied to a workflow execution — one sandbox per `workflowExecutionId`.

---

## Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Configuration](#configuration)
- [Sandbox Lifecycle](#sandbox-lifecycle)
- [Janitor / TTL Cleanup](#janitor--ttl-cleanup)
- [Security Model](#security-model)
- [API Reference](#api-reference)
- [Execution Sandbox (Docker Image)](#execution-sandbox-docker-image)
- [Remotion Template](#remotion-template)
- [Docker Build](#docker-build)

---

## Overview

A **sandbox** is a pair of:

1. A **Docker container** spawned from the `reelforge-sandbox-executor:local` image (configurable), running as an unprivileged `node` user with strictly limited network access (containers live on an isolated `sandbox-net` by default), a read-only root filesystem, dropped capabilities, and strict resource limits. The isolated network allows outbound requests to public registries such as npm but prevents any connectivity to the rest of the application stack.
2. A **host workspace directory** bind-mounted at `/workspace` inside the container, where all source files for the Remotion project live.

The workflow engine (`.NET`) interacts with this service to:

- Create a sandbox when a workflow execution starts.
- Write AI-generated Remotion component files into the workspace.
- Install npm packages required by those components.
- Run `npm run build`, `npx remotion render`, or other allowed scripts inside the container.
- Read back output artefacts (rendered video, build bundles, file listings).
- Delete the sandbox when the workflow is complete or on error.

---

## Architecture

```
┌────────────────────────────────────────────────────────────┐
│  Sandbox Service  (Go HTTP server, :8080)                  │
│                                                            │
│  sandboxManager (in-memory registry)                       │
│  ├── sandboxes map[id]*sandbox                             │
│  └── executionIndex map[workflowExecutionId]sandboxId      │
│                                                            │
│  apiHandler → Gorilla Mux routes                          │
│  janitor goroutine (TTL cleanup, every 1 min)              │
└──────────────────────────┬─────────────────────────────────┘
                           │  docker CLI subprocess calls
                           ▼
          ┌─────────────────────────────────┐
          │  Docker Engine (host socket)     │
          │                                  │
          │  Container: rf-sbx-<uuid>        │
          │   image: reelforge-sandbox-      │
          │          executor:local          │
          │   --network none                 │
          │   --read-only                    │
          │   --user node                    │
          │   -v /var/lib/reelforge/         │
          │      sandboxes/<uuid>:/workspace │
          └────────────────┬────────────────┘
                           │
                           ▼
             /var/lib/reelforge/sandboxes/<uuid>/
             (Remotion project source files)
```

The service itself is **stateless on disk** — all runtime state is kept in the `sandboxManager` in-memory map. Restarting the service orphans existing containers (they are not recovered). The janitor continuously cleans up containers whose `LastActivity` has exceeded the configured TTL.

---

## Configuration

All configuration is read from environment variables at startup. There are no config files.

| Variable | Default | Description |
|---|---|---|
| `PORT` | `8080` | HTTP port the service listens on |
| `SANDBOX_IMAGE` | `reelforge-sandbox-executor:local` | Docker image used for each sandbox container |
| `SANDBOX_ROOT` | `/var/lib/reelforge/sandboxes` | Host path where workspace directories are created |
| `SANDBOX_NETWORK` | `sandbox-net` | Docker network that sandbox containers use (isolated from main cluster, allows internet access) |
| `SANDBOX_TTL` | `1h` | Inactivity duration after which a sandbox is automatically destroyed (e.g. `30m`, `2h`) |
| `SANDBOX_EXEC_TIMEOUT` | `5m` | Default execution timeout for `docker exec` calls. Per-request `timeoutSeconds` can override this up to a maximum of `15m` |
| `SANDBOX_MEMORY_LIMIT` | `2g` | Docker `--memory` limit per container |
| `SANDBOX_CPU_LIMIT` | `2` | Docker `--cpus` limit per container |
| `SANDBOX_PIDS_LIMIT` | `256` | Docker `--pids-limit` per container |

Duration values accept Go `time.Duration` format strings: `30s`, `5m`, `1h30m`, etc.

---

## Sandbox Lifecycle

```
POST /api/v1/sandboxes
   │
   ├─ [idempotent] if a sandbox for this workflowExecutionId already exists → return it (HTTP 200)
   └─ [new] allocate UUID, mkdir workspace, docker run → register in memory (HTTP 201)
          │
          │  (AI agent writes files, installs packages, runs builds)
          │
POST /api/v1/sandboxes/{workflowExecutionId}/complete
   └─ docker rm -f container, rm -rf workspace, deregister from memory (HTTP 200)
```

Alternatively, DELETE can be used at any time:

```
DELETE /api/v1/sandboxes/{workflowExecutionId}   →  same as /complete
```

### Idempotent Creation

`POST /api/v1/sandboxes` is idempotent: if a sandbox for the given `workflowExecutionId` already exists, it updates `LastActivity` and returns the existing sandbox object with `HTTP 200`. A new sandbox returns `HTTP 201`.

### Container Initialization

On container start, the `sh` entrypoint copies the bundled Remotion template into `/workspace` if `package.json` is not already present:

```sh
if [ ! -f /workspace/package.json ]; then cp -a /opt/remotion-template/. /workspace/; fi; sleep infinity
```

This ensures the workspace is always bootstrapped with a valid Remotion project structure and that the `node_modules` from the pre-installed template are available immediately.

---

## Janitor / TTL Cleanup

A background goroutine runs every **60 seconds** and removes any sandbox whose `LastActivity` is older than `SANDBOX_TTL`. "Last activity" is updated on every successful operation: `create`, `exec`, `readFile`, `writeFile`, `listFiles`, `deletePath`, `installPackages`.

This means:
- A sandbox that is being actively operated on will never be collected.
- An idle sandbox (e.g. a workflow that crashed mid-execution without calling `/complete`) will eventually be cleaned up automatically, preventing resource leaks.

---

## Security Model

Each Docker container is launched with strict hardening flags:

| Flag | Effect |
|---|---|
| `--network none` | No network access — container cannot make outbound calls |
| `--read-only` | Root filesystem is read-only — only `/workspace` and `/tmp` are writable |
| `--tmpfs /tmp:rw,nosuid,nodev,size=256m` | Ephemeral `/tmp` limited to 256 MB |
| `--memory` | Hard memory cap (default 2 GB) |
| `--cpus` | CPU share limit (default 2 cores) |
| `--pids-limit` | Max OS processes (default 256), preventing fork bombs |
| `--security-opt no-new-privileges` | Prevents privilege escalation via `setuid` binaries |
| `--cap-drop ALL` | Drops all Linux capabilities |
| `--user node` | Runs as the unprivileged `node` user, not root |

### Command Allowlist

The `/exec` endpoint does **not** accept arbitrary commands. Only the following are permitted:

| Command | Allowed subcommands / arguments |
|---|---|
| `npm run` | `build`, `render`, `typecheck`, `compositions`, `lint` |
| `npx remotion` | `render`, `still`, `compositions` |

Any other command or argument combination returns `HTTP 400 Bad Request`. This prevents arbitrary code execution from escaping the container via the API.

### Path Traversal Prevention

All file system operations resolve the given relative path against the sandbox workspace root and verify the resolved absolute path starts with the workspace root prefix. Any attempt to escape via `../` sequences is rejected with `HTTP 400`.

The workspace root itself cannot be deleted via the `DELETE /files` endpoint.

### Package Name Validation

The `/packages` install endpoint validates each package name against the regex:

```
^(@[a-z0-9\-_]+/)?[a-z0-9\-_.]+(@[a-zA-Z0-9.\-_]+)?$
```

This allows standard and scoped npm package names (e.g. `react`, `@remotion/cli`, `d3@7.0.0`) while rejecting shell injection characters.

---

## API Reference

All endpoints are prefixed with `/api/v1/sandboxes`. The `{workflowExecutionId}` path parameter must be a valid UUID v4.

### Health Check

```
GET /health
```

Response `200 OK`:
```json
{ "status": "ok" }
```

---

### Create Sandbox

```
POST /api/v1/sandboxes
Content-Type: application/json

{
  "workflowExecutionId": "550e8400-e29b-41d4-a716-446655440000"
}
```

- Returns `201 Created` for a new sandbox, `200 OK` if one already exists for the given execution ID.

Response body:
```json
{
  "id": "7d6e2f1a-...",
  "workflowExecutionId": "550e8400-...",
  "containerName": "rf-sbx-7d6e2f1a-...",
  "workspacePath": "/var/lib/reelforge/sandboxes/7d6e2f1a-...",
  "createdAt": "2026-03-07T10:00:00Z",
  "lastActivity": "2026-03-07T10:00:00Z"
}
```

---

### List Sandboxes

```
GET /api/v1/sandboxes
```

Response `200 OK`: array of sandbox objects (same schema as above).

---

### Get Sandbox

```
GET /api/v1/sandboxes/{workflowExecutionId}
```

Response `200 OK`: single sandbox object.  
Response `404 Not Found` if not found.

---

### Delete Sandbox

```
DELETE /api/v1/sandboxes/{workflowExecutionId}
```

Stops and removes the container, deletes the workspace directory, and deregisters the sandbox.

Response `200 OK`:
```json
{ "ok": true }
```

---

### Get Sandbox Status

```
GET /api/v1/sandboxes/{workflowExecutionId}/status
```

Returns readiness information without requiring the sandbox to exist.

Response `200 OK` (sandbox not found):
```json
{ "exists": false }
```

Response `200 OK` (sandbox found):
```json
{
  "exists": true,
  "ready": true,
  "hasPackageJson": true,
  "hasNodeModules": true,
  "containerName": "rf-sbx-7d6e2f1a-...",
  "workspacePath": "/var/lib/reelforge/sandboxes/7d6e2f1a-...",
  "createdAt": "2026-03-07T10:00:00Z",
  "lastActivity": "2026-03-07T10:02:30Z"
}
```

`ready` is `true` when both `package.json` and `node_modules/` exist in the workspace — meaning the project can be built or rendered immediately.

---

### Complete Workflow (Delete Sandbox)

```
POST /api/v1/sandboxes/{workflowExecutionId}/complete
```

Functionally identical to `DELETE /api/v1/sandboxes/{workflowExecutionId}`. Intended to be called by the workflow engine when execution finishes (success or failure) to signal explicit resource cleanup.

Response `200 OK`:
```json
{ "ok": true }
```

---

### Execute Command

```
POST /api/v1/sandboxes/{workflowExecutionId}/exec
Content-Type: application/json

{
  "command": "npm",
  "args": ["run", "build"],
  "timeoutSeconds": 120
}
```

Runs the given command inside the sandbox container via `docker exec`. `timeoutSeconds` is optional; defaults to `SANDBOX_EXEC_TIMEOUT`. Maximum is 900 seconds (15 minutes).

**Allowed commands:**

| `command` | `args[0]` | `args[1]` |
|---|---|---|
| `npm` | `run` | `build` \| `render` \| `typecheck` \| `compositions` \| `lint` |
| `npx` | `remotion` | `render` \| `still` \| `compositions` |

Response `200 OK`:
```json
{ "output": "...(stdout+stderr combined)..." }
```

Response `400 Bad Request` (command not allowed):
```json
{ "error": "command not allowed", "output": "" }
```

Response `500 Internal Server Error` (execution failure):
```json
{ "error": "execution failed: exit status 1", "output": "...(stdout+stderr)..." }
```

---

### Install npm Packages

```
POST /api/v1/sandboxes/{workflowExecutionId}/packages
Content-Type: application/json

{
  "packages": ["framer-motion", "@remotion/shapes@4.0.0"]
}
```

Runs `npm install --save <packages...>` inside the sandbox container. Each package name is validated against the allowlist regex before execution.

Response `200 OK`:
```json
{ "output": "...(npm install output)..." }
```

Response `400 Bad Request` if any package name is invalid.

---

### List Files

```
GET /api/v1/sandboxes/{workflowExecutionId}/files?path=src
```

Lists directory contents at the given relative path within the workspace. `path` defaults to the workspace root if omitted.

Response `200 OK`:
```json
[
  { "name": "index.ts",    "isDir": false, "size": 82,   "modTime": "2026-03-07T10:01:00Z" },
  { "name": "root.tsx",    "isDir": false, "size": 654,  "modTime": "2026-03-07T10:01:00Z" },
  { "name": "components",  "isDir": true,  "size": 4096, "modTime": "2026-03-07T10:02:00Z" }
]
```

---

### Get File Content

```
GET /api/v1/sandboxes/{workflowExecutionId}/files/content?path=src/root.tsx
```

Reads a file and returns its content Base64-encoded. `path` is required.

Response `200 OK`:
```json
{
  "path": "src/root.tsx",
  "contentBase64": "aW1wb3J0IHR5cGUgUmVhY3QuLi4="
}
```

---

### Write File Content

```
PUT /api/v1/sandboxes/{workflowExecutionId}/files/content?path=src/components/Hero.tsx
Content-Type: application/json

{
  "contentBase64": "aW1wb3J0IHR5cGUgUmVhY3QuLi4="
}
```

Creates or overwrites a file at the given relative path. Parent directories are created automatically. Content must be Base64-encoded. `path` is required.

Response `200 OK`:
```json
{ "ok": true }
```

---

### Delete File or Directory

```
DELETE /api/v1/sandboxes/{workflowExecutionId}/files?path=src/old-component.tsx
```

Deletes a file or directory (recursively) at the given path. `path` is required. Deleting the workspace root is not allowed.

Response `200 OK`:
```json
{ "ok": true }
```

---

## Execution Sandbox (Docker Image)

The runtime container image (`reelforge-sandbox-executor:local`) must be pre-built and available on the Docker host. It is **not** built by the sandbox service itself. The image must provide:

- Node.js 22 + npm
- Chromium (for Remotion's headless renderer)
- ffmpeg (for video encoding)
- The Remotion template pre-installed at `/opt/remotion-template/` (including `node_modules`)
- A non-root `node` user

The service's own `Dockerfile` (see below) also builds this image as part of a multi-stage build for convenience in local development and CI.

---

## Remotion Template

The `/sandbox/template/` directory contains the bootstrapped Remotion project that is copied into every new sandbox workspace. It is baked into the runtime container image at `/opt/remotion-template`.

> **Compatibility hack:** When a workspace is created the sandbox manager also establishes a
> symlink at
> `/workspace/node_modules/.remotion/chrome-headless-shell/linux64/chrome-headless-shell-linux64/chrome-headless-shell`
> pointing at the system Chromium binary. This makes any headless-shell path that Remotion
> downloads resolve correctly, ensuring renders succeed even if the CLI still falls back to
> the downloaded binary.

### Structure

```
template/
├── package.json
├── tsconfig.json
└── src/
    ├── index.ts        # Remotion entry point — calls registerRoot(Root)
    └── root.tsx        # Default composition: 6-second 1920×1080 intro animation
```

### `index.ts`

Registers the root component with Remotion:

```ts
import { registerRoot } from 'remotion';
import { Root } from './root';
registerRoot(Root);
```

### `root.tsx`

Defines the default `Root` component and a single `Main` composition (1920×1080, 30 fps, 180 frames = 6 s). The composition renders a violet radial-gradient background with the "ReelForge" wordmark fading in and gently translating vertically. AI agents replace/extend this file with project-specific content.

### `package.json` — Preinstalled Dependencies

| Package | Purpose |
|---|---|
| `remotion` + `@remotion/cli` + `@remotion/renderer` | Core Remotion framework and CLI |
| `@remotion/google-fonts` | Google Fonts integration for Remotion |
| `react` + `react-dom` | React 18 (peer dep of Remotion) |
| `@react-spring/web` | Physics-based animation library |
| `framer-motion` | Declarative animation library |
| `@react-three/fiber` + `@react-three/drei` | React bindings for Three.js |
| `three` | 3D graphics library |
| `d3` | Data-driven visualisations |

### `package.json` — Available Scripts

| Script | Command | Description |
|---|---|---|
| `build` | `remotion bundle src/index.ts --out-dir build` | Bundle to static assets |
| `render` | `remotion render src/index.ts Main out/video.mp4 --chromium-executable=/usr/bin/chromium` | Full video render via Chromium (explicit path ensures container-installed binary is used) |
| `compositions` | `remotion compositions src/index.ts` | List registered compositions |
| `typecheck` | `tsc --noEmit` | TypeScript type check only |

---

## Docker Build

The `sandbox/Dockerfile` performs a two-stage build:

### Stage 1 — Go binary (`golang:1.25-alpine`)

1. Downloads Go module dependencies with retry logic (up to 5 attempts).
2. Compiles the Go server as a static binary: `CGO_ENABLED=0 go build -ldflags="-s -w -X main.Version=<VERSION>"`.
3. The `VERSION` build arg (defaults to `docker`) is injected into the `main.Version` variable, which is logged on startup.

### Stage 2 — Runtime image (`node:22-alpine`)

1. Installs system packages: `docker-cli`, `chromium`, `ffmpeg`, and font libraries (nss, freetype, harfbuzz, ttf-freefont).
2. Sets `PUPPETEER_EXECUTABLE_PATH=/usr/bin/chromium` so Remotion/Puppeteer finds the bundled Chromium.
3. Copies `template/package.json` and runs `npm install` to pre-install all Remotion dependencies into the image at `/opt/remotion-template/node_modules`.
4. Copies the rest of the template source files.

    > **Troubleshooting:** When workspaces run `npx remotion render` directly the CLI
    > defaults to a bundled `chrome-headless-shell` binary under
    > `/workspace/node_modules/.remotion/...`. That file is **not** included in the Alpine
    > image and will cause `ENOENT` errors like the one seen in workflow logs. The Go toolkit
    > and template scripts automatically append `--chromium-executable=/usr/bin/chromium` to
    > render invocations to avoid this issue. If you execute remotion manually, add the flag
    > yourself or set `REMOTION_CHROMIUM_EXECUTABLE`.
5. Copies the Go binary from Stage 1.
6. Exposes port `8080` and sets the entrypoint to the Go binary.

> **Note:** The final runtime image serves a dual purpose — it is both the **sandbox service** (running the Go HTTP server) and the **sandbox executor image** (used as the runtime container for each isolated workspace). In production, these may be the same image, or the executor image may be a separate, more minimal build.
