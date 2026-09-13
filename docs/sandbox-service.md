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

1. A **Docker container** spawned from the minimal `reelforge-sandbox-runtime:local` image (configurable), running as an unprivileged `node` user with a read-only root filesystem, all capabilities dropped, strict resource limits, and — by default — **no network route out at all** (`sandbox-net` is created `--internal`). See [Security Model](#security-model); the code running in that container is assumed hostile.
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
          │          runtime:local           │
          │   --network sandbox-net          │
          │        (--internal: no route out)│
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
| `SANDBOX_API_TOKEN` | *(none — required)* | Shared secret required on every `/api/v1/sandboxes/*` request. The service **exits at startup** if unset |
| `SANDBOX_IMAGE` | `reelforge-sandbox-runtime:local` | Docker image used for each sandbox container. Must be the minimal runtime image, never the control-plane image |
| `SANDBOX_ROOT` | `/var/lib/reelforge/sandboxes` | Host path where workspace directories are created |
| `SANDBOX_NETWORK` | `sandbox-net` | Docker network sandbox containers attach to. Created by this service, with `--internal` unless egress is enabled. Not declared in `docker-compose.yml` — compose would prefix the name and create a different, unused network |
| `SANDBOX_NETWORK_EGRESS` | `false` | When `false`, the sandbox network is created `--internal`: no internet and no route to the host gateway. When `true`, sandboxed code can reach the network and `POST /packages` is enabled |
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

### Threat model — start here

**Assume arbitrary code is running inside every sandbox container.** This is not a
risk to be mitigated; it is the feature. Agents write TSX and install npm packages,
both of which execute. The `/exec` allowlist does *not* change this: `npm run build`
runs whatever `package.json` says, and `package.json` is a file the caller writes
through `PUT /files/content`. A caller who can reach this API can run any command
inside the container, by design.

Everything below therefore assumes the container is hostile and asks only one
question: *what can it reach?* Two boundaries carry the entire security model —
**the container boundary** and **the network boundary**. The command allowlist and
the package-name regex are input hygiene. They are not containment, and no
security decision should rest on them.

The asset being protected is the **host**. The sandbox service holds
`/var/run/docker.sock`, so code execution in the *service* is equivalent to root on
the host. The service is the crown jewel; the sandbox containers are the untrusted
zone; nothing should ever flow from the second to the first.

### Network boundary

`SANDBOX_NETWORK` (`sandbox-net`) is created **by this service, with `--internal`**,
unless `SANDBOX_NETWORK_EGRESS=true`.

`--internal` is doing the real work. Without it, a Docker bridge gives the container
a default route to the host gateway — and **every port published by `docker
compose` is bound on that gateway**. "Isolated on its own Docker network" is worth
nothing on its own: a sandbox on a normal bridge can reach the host's published
Postgres, RabbitMQ, MinIO, and nginx just by addressing the gateway IP. With
`--internal` there is no default route at all: no internet, no other network, no
host.

Reinforcing that, `docker-compose.yml` publishes every port except nginx's on
`127.0.0.1` only, so nothing but the reverse proxy is reachable from off-host or
from a container that somehow acquires a route.

The service **refuses to start** if a network of that name already exists without
`Internal: true` — otherwise a leftover network from an older release would
silently restore egress.

**Enabling egress** (`SANDBOX_NETWORK_EGRESS=true`) is what makes
`POST /packages` work, because npm needs the registry. It also gives
attacker-controlled code a network. Prefer adding dependencies to the runtime
image; with egress off, `POST /packages` returns `409 Conflict` explaining this
rather than hanging until the exec timeout.

### Container boundary

| Flag | Effect |
|---|---|
| `--network sandbox-net` (`--internal`) | No default route: no internet, no host gateway, no other Docker network. See above |
| `--read-only` | Root filesystem is read-only — only `/workspace` and the tmpfs mounts are writable |
| `--tmpfs /tmp:rw,nosuid,nodev,size=256m` | Ephemeral `/tmp` limited to 256 MB, no setuid, no device nodes |
| `--memory` | Hard memory cap (default 2 GB) |
| `--cpus` | CPU share limit (default 2 cores) |
| `--pids-limit` | Max OS processes (default 256), preventing fork bombs |
| `--security-opt no-new-privileges` | Prevents privilege escalation via `setuid` binaries |
| `--cap-drop ALL` | Drops all Linux capabilities |
| `--user node` | Runs as the unprivileged `node` user, not root |

Note what this list does **not** include: user-namespace remapping and a custom
seccomp/AppArmor profile. A kernel-level container escape is therefore still an
escape to the host. Sandbox containers are hardened, not virtualised; if you need a
hard boundary against a kernel exploit, run the Docker host in a dedicated VM.

### Two images, not one

The image untrusted code runs in (`reelforge-sandbox-runtime:local`, the
`sandbox-runtime` build target) is **not** the image the service runs as
(`reelforge-sandbox-executor:local`, the `control-plane` target). These were the
same image previously, which meant every sandbox shipped with the Docker CLI,
`curl`, `wget`, `gnupg`, and the service binary. A Docker client inside the
untrusted container earns nothing for rendering and turns any reachable daemon
endpoint into instant host root.

Keep the runtime target minimal. Anything added to it is added to the attacker's
toolkit.

### API authentication

Every `/api/v1/sandboxes/*` route requires `Authorization: Bearer $SANDBOX_API_TOKEN`,
compared in constant time. `SANDBOX_API_TOKEN` has **no default** and the service
exits at startup if it is unset.

This API is **never exposed through nginx**. Its only client is the workflow engine,
in-cluster over the `reelforge` network. Do not add an nginx `location` for it: it
was previously proxied at `/api/v1/sandboxes` with no auth of any kind, which made
"write a `package.json`, then `POST /exec`" an unauthenticated remote code execution
path from the public internet into the container holding the Docker socket.

`/health` stays unauthenticated for container healthchecks and returns no state.

### Path traversal and symlinks

All file operations go through **`os.Root`** rooted at the workspace, which enforces
containment per path component at the syscall level and refuses any traversal that
leaves the root — including through a symlink.

A lexical check (`filepath.Clean` plus a prefix comparison) is *not* sufficient here
and was the previous implementation's flaw. Code inside the sandbox can create
`/workspace/escape -> /`; the service then resolves that link **in its own mount
namespace**, which is where the Docker socket is mounted. That turned "arbitrary
code in the sandbox" into arbitrary read/write in the control plane, and from there
into root on the host. `removeAllIn` likewise uses `Lstat`, so deleting a symlink
unlinks the link and never recurses into its target.

The workspace root itself cannot be written or deleted through the API.

### Input hygiene (not containment)

The `/exec` allowlist (`npm run <build|render|typecheck|compositions|lint>`,
`npx remotion <render|still|compositions>`) and the package-name regex

```
^(@[a-z0-9][a-z0-9\-_.]*/)?[a-z0-9][a-z0-9\-_.]*(@[a-zA-Z0-9.\-_]+)?$
```

keep honest callers on the intended path and stop malformed input reaching a
subprocess. The regex requires an alphanumeric first character specifically so a
"package" named `--foo` cannot be spliced into `npm install --save` as a flag; the
install command additionally passes `--` before the package list.

Neither mechanism constrains what ultimately executes inside the container. Treat
them as validation, never as a security boundary.

---

## API Reference

All endpoints are prefixed with `/api/v1/sandboxes`. The `{workflowExecutionId}` path parameter must be a valid UUID v4.

**Every endpoint below requires `Authorization: Bearer $SANDBOX_API_TOKEN`** and
returns `401 Unauthorized` without it. `GET /health` is the only unauthenticated
route. None of these are reachable through nginx — this API is in-cluster only.

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

Runs `npm install --save -- <packages...>` inside the sandbox container. Each package name is validated against the allowlist regex before execution.

Response `200 OK`:
```json
{ "output": "...(npm install output)..." }
```

Response `400 Bad Request` if any package name is invalid.

Response `409 Conflict` when `SANDBOX_NETWORK_EGRESS=false` (the default): the
sandbox network has no route to the registry, so the install cannot succeed. Bake
the dependency into the runtime image instead of enabling egress where possible.

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
| `render` | `remotion render src/index.ts Main out/video.mp4 --chromium-executable=/workspace/node_modules/.remotion/chrome-headless-shell/linux64/chrome-headless-shell-linux64/chrome-headless-shell` | Full video render via bundled headless shell (preferred over system Chrome) |
| `compositions` | `remotion compositions src/index.ts` | List registered compositions |
| `typecheck` | `tsc --noEmit` | TypeScript type check only |

---

## Docker Build

The `sandbox/Dockerfile` performs a three-stage build with two publishable
targets: `sandbox-runtime` (untrusted, what sandboxes run) and `control-plane`
(trusted, what the service runs). `docker-compose.yml` builds both — the
`sandbox-runtime` service builds the runtime image and exits, and
`sandbox-executor` waits on it via `service_completed_successfully`, because the
image must exist on the daemon before any `docker run` of it can succeed.

### Stage 1 — `gobuild` (`golang:1.25-alpine`)

1. Downloads Go module dependencies with retry logic (up to 5 attempts).
2. Compiles the Go server as a static binary: `CGO_ENABLED=0 go build -ldflags="-s -w -X main.Version=<VERSION>"`.
3. The `VERSION` build arg (defaults to `docker`) is injected into the `main.Version` variable, which is logged on startup.

### Stage 2 — `sandbox-runtime` (`node:22-bookworm-slim`) — the untrusted image

1. Installs system packages via `apt-get` (this is a Debian base, not Alpine): `ffmpeg`, the
   Chrome/Chromium *dependency* libraries Remotion's headless renderer needs at runtime
   (`libnss3`, `libatk-bridge2.0-0`, `libgbm1`, `libgtk-3-0`, etc. — there is no `chromium`
   browser package installed directly), and font packages (`fonts-liberation`,
   `fonts-noto-color-emoji`). Note there is no separate `PUPPETEER_EXECUTABLE_PATH` set in the
   image — see the troubleshooting note below for how the actual headless binary is resolved.
2. Copies `template/package.json` and runs `npm install` to pre-install all Remotion dependencies
   into the image at `/opt/remotion-template/node_modules` — this is what actually provisions the
   `chrome-headless-shell` binary Remotion renders with (see below), not a system package.
3. Copies the rest of the template source files and drops to the `node` user.

   Deliberately absent: the Docker CLI, `curl`, `wget`, `gnupg`. See
   [Two images, not one](#two-images-not-one).

    > **Troubleshooting:** When workspaces run `npx remotion render` directly the CLI
    > defaults to a bundled `chrome-headless-shell` binary under
    > `/workspace/node_modules/.remotion/...`, normally provisioned by Remotion itself during
    > `npm install` (step 3 above/the per-workspace install). If that binary is missing —
    > e.g. `npm install` was interrupted or skipped — `sandboxManager` falls back to
    > symlinking `/usr/bin/chromium` into that path so stray spawn attempts still succeed. The
    > Go toolkit and template scripts additionally append
    > `--chromium-executable=/workspace/node_modules/.remotion/chrome-headless-shell/linux64/chrome-headless-shell-linux64/chrome-headless-shell`
    > to render invocations to avoid `ENOENT` errors. If you execute remotion manually, add the
    > flag yourself or set `REMOTION_CHROMIUM_EXECUTABLE`.
### Stage 3 — `control-plane` (`FROM sandbox-runtime`) — the trusted service image

1. Returns to `root` and installs `curl` + `gnupg`, then Docker's official
   `docker-ce-cli` from `download.docker.com`'s apt repo — the `docker` binary this
   service shells out to in order to launch each per-execution sandbox container.
2. Copies the Go binary from Stage 1.
3. Exposes port `8080` and sets the entrypoint to the Go binary.

Building on the runtime stage is purely for layer reuse. Nothing added here may
ever move up into Stage 2.

> **Note:** The two targets are **not** interchangeable. `sandbox-runtime` is what
> untrusted code executes in and must stay minimal; `control-plane` adds the Docker
> CLI and the Go binary and holds the Docker socket. They were previously a single
> image, which put a Docker client inside every untrusted container. `SANDBOX_IMAGE`
> must always point at the runtime target.
