# ReelForge Copilot Instructions (Repository-Wide)

These instructions are the default baseline for the entire ReelForge monorepo.
Primary audience is the Copilot coding agent (not general human onboarding docs).
They are intentionally broad and architecture-focused. Add narrower, file-pattern-specific guidance in `.github/instructions/*.instructions.md` using `applyTo` frontmatter.

## 1) Project Overview

ReelForge is a microservices platform that generates promotional videos from source projects through an agentic workflow pipeline.

High-level flow:
1. Users authenticate via the Go API.
2. Users manage projects/files/agents/workflows via the Inference API.
3. Workflow execution requests are published to RabbitMQ.
4. Workflow Engine consumes requests, orchestrates AI agents, and can execute Remotion build/render tasks in isolated sandbox containers.
5. Frontend (Next.js) consumes the APIs through nginx, which is the single entry point.

## 2) System Architecture

### Edge and routing
- `nginx` is the single public entry point (port `80`, configurable by `APP_PORT`).
- `nginx/auth.js` translates auth cookies into `Authorization: Bearer` headers for upstream services.
- Browser talks to nginx only; nginx forwards to internal services.

### Core services
- `web/` (`Next.js 15`, App Router, Mantine v8): UI for authentication, projects, files, workflows, agents, admin.
- `api/` (Go, Gorilla Mux, GORM): auth authority, user/admin endpoints, workflow stats/events (SSE).
- `inference/src/ReelForge.Inference.Api/` (.NET 9): CRUD for projects/files/agents/workflows, file summarization, publishes execution requests.
- `inference/src/ReelForge.WorkflowEngine/` (.NET 9): executes workflows, coordinates built-in agents, consumes/publishes MassTransit events.
- `sandbox/` (Go): sandbox lifecycle and command execution in hardened Docker containers for Remotion-related build/render work.

### Infrastructure
- PostgreSQL 16: shared DB with strict table ownership split between .NET services.
- RabbitMQ (with management UI): async communication and workflow event backbone.
- MinIO + init container: S3-compatible object storage for project artifacts/files.
- Docker Compose: local orchestration of all services and dependencies.

## 3) Data Ownership and Boundaries

### Authentication boundary
- Go API is the only service that issues JWTs.
- JWT claims include `sub`, `email`, `isAdmin`.
- Inference services validate JWTs; they do not mint user tokens.

### Database ownership boundary
- Do **not** mix migration ownership across .NET services.
- Inference API owns (examples): `application_users`, `projects`, `project_files`, `agent_definitions`.
- Workflow Engine owns (examples): `workflow_definitions`, `workflow_steps`, `workflow_executions`, `workflow_step_results`, `review_scores`.
- Go API reads/writes user-related data through GORM models but does not drive schema via auto-migrate.

### Messaging boundary
- Inference API publishes `WorkflowExecutionRequested`.
- Workflow Engine consumes request and publishes completion/step/failure events.
- Prefer event-driven communication over tight service coupling.

## 4) Tech Stack by Service

### Frontend (`web/`)
- Next.js 15 App Router
- React + TypeScript
- Mantine v8
- SWR for server-state fetching/caching
- `@tabler/icons-react`, `@dnd-kit/sortable`

### Go API (`api/`)
- Go
- Gorilla Mux routing
- GORM + PostgreSQL
- JWT auth (HS256)
- SSE endpoints for workflow events

### Inference + Workflow Engine (`inference/`)
- .NET 9 Web APIs
- EF Core
- MassTransit + RabbitMQ
- OpenTelemetry instrumentation
- Azure OpenAI integration

### Sandbox (`sandbox/`)
- Go HTTP API controlling Docker containers
- Ephemeral per-workflow sandbox workspaces
- Hardened execution constraints (resource limits, allowlisted commands, traversal protections)

### Runtime / infra
- Docker Compose multi-service stack
- nginx reverse proxy and auth script
- PostgreSQL, RabbitMQ, MinIO

## 5) Repository Structure

- `web/`: frontend app
- `api/`: Go auth/admin/workflow-stats API
- `inference/`: .NET solution (`Shared`, `Inference.Api`, `WorkflowEngine`, tests)
- `sandbox/`: sandbox executor service + remotion template assets
- `nginx/`: reverse proxy config + auth translation script
- `docs/`: principal project documentation
- `.github/`: Copilot instructions, prompts, workflows

Keep architecture docs and workflow docs in `docs/` updated when behavior changes.

## 6) Coding Guidelines (Cross-Repo)

- Prefer clear, maintainable, testable code over clever code.
- Keep functions single-purpose and avoid deep nested logic where possible.
- Respect existing style in each service; do not force one language style onto another service.
- Do not introduce breaking API/DTO contract changes unless explicitly requested.
- Preserve established naming and payload conventions per service.
- Avoid speculative refactors unrelated to the task.
- Use `npm` for JavaScript/TypeScript package management in this repository.
- If requirements are ambiguous or confidence is low, ask the human for guidance early using question prompts instead of guessing.

## 7) Service-Specific Rules

### Frontend (`web/`)
- Use App Router conventions and existing route grouping patterns.
- Keep UI changes consistent with Mantine-based design and existing component patterns.
- Keep data fetching concerns in `lib/api` and `lib/hooks` patterns.
- Keep TypeScript types centralized in `lib/types` unless there is a stronger local pattern.

### Go API (`api/`)
- Add routes in `handlers/` and register through the central route registration path.
- Keep JSON payload fields camelCase.
- Maintain middleware chain behavior (`Auth`, then `Admin` where required).
- Keep business logic in `services/`, not handlers.

### .NET services (`inference/`)
- Keep shared contracts/models in `ReelForge.Shared` when used by both .NET services.
- Keep API concerns in `ReelForge.Inference.Api`; execution concerns in `ReelForge.WorkflowEngine`.
- Maintain MassTransit event contracts compatibility.
- Respect existing step execution strategy abstractions.

### Sandbox (`sandbox/`)
- Preserve security model and command allowlist.
- Do not weaken isolation defaults without explicit requirement.
- Keep workflow-execution idempotency and cleanup behavior intact.

## 8) Build, Test, and Local Commands

Use service-local commands from the relevant folder.

- Go API:
  - `go run .`
  - `go test ./...`
- Inference:
  - `dotnet build ReelForge.sln`
  - `dotnet test`
- Frontend (`web`):
  - `npm run dev`
  - `npm run build`
  - `npm run lint`
- Full stack:
  - `docker compose up --build -d`

Prefer existing scripts and documented commands over inventing new ad-hoc flows.

## 9) Required Quality Gates Before Merge

Unless explicitly waived by maintainers, changes should satisfy:

- Go API tests: `go test ./...` (from `api/`)
- .NET tests: `dotnet test` (from `inference/`)
- Frontend lint: `npm run lint` (from `web/`)
- Compose smoke check: `docker compose up --build -d` and core service health checks

## 10) API, Contracts, and Safety Expectations

- Treat auth and workflow execution paths as safety-critical.
- Validate input and preserve existing authorization checks.
- When changing events/DTOs, update producers and consumers together.
- Keep error responses actionable and logs meaningful.
- For unknowns, choose the least risky change and document assumptions.

## 10) Resource Pointers for Agents

- Root architecture and operational context: `CLAUDE.md`
- Sandbox internals and API: `docs/sandbox-service.md`
- Built-in workflow agents and outputs: `docs/builtin-agents.md`
- Feature and implementation docs: `docs/*.md`
- Compose topology and runtime env wiring: `docker-compose.yml`

## 11) Human-in-the-Loop Rule

- When uncertain, blocked, or facing multiple valid implementations, ask the human concise clarifying questions before changing code.
- Prefer explicit confirmation for potentially risky changes (schema updates, auth behavior, message contracts, destructive operations).
- Better to ask one focused question than to proceed on assumptions.

## 12) Instruction Layering Strategy

Use this file as the generic default. For tighter scope, add specialized instruction files:

- Path: `.github/instructions/<name>.instructions.md`
- Use frontmatter `applyTo` patterns (for example: `web/**/*.tsx`, `api/**/*.go`, `inference/**/*.cs`, `sandbox/**/*.go`, `**/*test*`).
- Put only scope-specific rules in those files; keep this root file as the common baseline.

When rules conflict, follow the most specific file that matches the current file/task.