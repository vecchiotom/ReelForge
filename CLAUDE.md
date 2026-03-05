# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

ReelForge is a microservices platform for generating promotional videos using [Remotion](https://www.remotion.dev/) via agentic workflows. Four services coordinate to handle the frontend, API requests, and AI/agent inference:

- **`/web`** — Next.js 15 App Router + Mantine v8 frontend
- **`/api`** — Go REST API (Gorilla Mux, GORM, PostgreSQL)
- **`/inference/src/ReelForge.Inference.Api`** — .NET 9 REST API for projects, files, agents, workflows CRUD
- **`/inference/src/ReelForge.WorkflowEngine`** — .NET 9 workflow execution engine consuming from RabbitMQ

All services are containerized and accessed through an nginx reverse proxy on a single port.

## Architecture

```
                         ┌──────────────┐
                         │    Nginx     │ :80
                         └──────┬───────┘
                    ┌───────────┼───────────┐
                    ▼           ▼           ▼
              ┌──────────┐ ┌────────┐ ┌─────────┐
              │  Go API  │ │  Web   │ │Inference│
              │  (auth)  │ │(Next)  │ │   API   │
              └──────────┘ └────────┘ └────┬────┘
                                           │ MassTransit/RabbitMQ
                                           ▼
                                    ┌─────────────┐
                                    │  Workflow    │
                                    │  Engine      │
                                    └──────┬──────┘
                    ┌──────────────────────┼────────────────┐
                    ▼                      ▼                ▼
              ┌──────────┐         ┌─────────────┐   ┌───────────┐
              │PostgreSQL│         │Azure OpenAI │   │  RabbitMQ  │
              └──────────┘         └─────────────┘   └───────────┘
```

Nginx is the single entry point (port 80). It routes requests to the appropriate backend and translates httpOnly cookies into Authorization headers. The Go API is the authority for user management and JWT issuance. The Inference API handles CRUD and publishes execution requests to RabbitMQ. The Workflow Engine consumes execution requests and runs AI agents.

### Go API

**Module:** `github.com/vecchiotom/reelforge`
**Routing:** Gorilla Mux — new routes belong in `handlers/` with a `Register*Routes(router)` function, called from `handlers/handlers.go:RegisterHandlers()`
**Database:** PostgreSQL via GORM — models go in `models/`, business logic in `services/`. **No auto-migrate** — inference owns the schema via EF Core migrations.
**Auth:** Go API is the sole authority for user management and JWT issuance (HS256). JWTs include `sub`, `email`, `isAdmin` claims (camelCase). Inference service validates these tokens.
**JSON convention:** All Go API JSON tags use camelCase (e.g. `accessToken`, `mustChangePassword`, `isAdmin`).

#### Go API Structure

```
api/
├── config/config.go              # AppConfig struct, env var loading
├── database/database.go          # GORM PostgreSQL connection (no auto-migrate)
├── models/user.go                # ApplicationUser GORM model
├── services/
│   ├── user_service.go           # User CRUD, bcrypt password ops, OTP generation, admin seeding
│   ├── jwt_service.go            # HS256 JWT generation + validation
│   └── smtp_service.go           # Optional SMTP email (falls back to console logging)
│   └── rabbitmq_service.go       # RabbitMQ consumer + in-memory SSE hub (workflow events)
├── middleware/
│   ├── auth.go                   # Bearer token extraction, validation, UserContext injection
│   └── admin.go                  # IsAdmin check (403 if not admin)
├── handlers/
│   ├── handlers.go               # Route registration hub with subrouter middleware chaining
│   ├── workflows.go              # GET /api/v1/workflows/stats + GET /api/v1/workflows/events (SSE)
│   ├── health/health.go          # GET /health
│   ├── auth/
│   │   ├── auth.go               # POST /api/v1/auth/token, POST /api/v1/auth/change-password
│   │   └── dto.go                # TokenRequest, TokenResponse, ChangePasswordRequest
│   └── admin/
│       ├── users.go              # Admin user CRUD (POST/GET/PUT/DELETE /api/v1/admin/users)
│       └── dto.go                # CreateUserRequest/Response, UpdateUserRequest, UserResponse
├── models/
│   ├── user.go                   # ApplicationUser GORM model
│   └── workflow_execution.go     # WorkflowExecution GORM model (read-only, WorkflowEngine-owned table)
├── main.go                       # Entry point: config load, DB init, admin seed, RabbitMQ consumer, server start
├── Dockerfile                    # Multi-stage Go build
├── go.mod / go.sum
```

#### Go API Endpoints

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `GET` | `/health` | Public | Health check |
| `POST` | `/api/v1/auth/token` | Public | Login (email + password → JWT) |
| `POST` | `/api/v1/auth/change-password` | Authenticated | Change password (clears `must_change_password`) |
| `GET` | `/api/v1/workflows/stats` | Authenticated | Workflow execution aggregate stats (queued/active/completed/failed) |
| `GET` | `/api/v1/workflows/events` | Authenticated | SSE stream of real-time workflow events (`execution.completed`, `execution.failed`, `step.completed`) |
| `POST` | `/api/v1/admin/users` | Admin | Create user (returns temporary password, sends email if SMTP configured) |
| `GET` | `/api/v1/admin/users` | Admin | List all users |
| `GET` | `/api/v1/admin/users/{id}` | Admin | Get single user |
| `PUT` | `/api/v1/admin/users/{id}` | Admin | Update user (optional `reset_password` flag) |
| `DELETE` | `/api/v1/admin/users/{id}` | Admin | Delete user |

#### Auth Flow

1. On first startup with empty DB, an admin user is seeded from `ADMIN_EMAIL`/`ADMIN_PASSWORD` env vars (or auto-generated + logged)
2. Admin creates users via `/api/v1/admin/users` — each gets a random 16-char OTP (emailed via SMTP or logged to console)
3. New users login via `/api/v1/auth/token` — response includes `mustChangePassword: true`
4. User changes password via `/api/v1/auth/change-password` — clears the flag
5. JWT tokens (24h expiry) are used for all authenticated endpoints in both Go API and inference services

#### Middleware Chain

- **Public routes:** `/health`, `/api/v1/auth/token` — no middleware
- **Authenticated routes:** `/api/v1/auth/change-password` — `middleware.Auth` (validates JWT, injects `UserContext`)
- **Admin routes:** `/api/v1/admin/*` — `middleware.Auth` → `middleware.Admin` (checks `IsAdmin`)

### Inference Service (Split Architecture)

The inference layer is split into two microservices sharing a common `ReelForge.Shared` library:

**Solution:** `inference/ReelForge.sln` containing three projects:
- `ReelForge.Shared` — Class library with models, enums, integration events, `ICurrentUser` interface
- `ReelForge.Inference.Api` — REST API for CRUD operations, file summarization
- `ReelForge.WorkflowEngine` — Workflow execution engine consuming from RabbitMQ

**Communication:** MassTransit over RabbitMQ with automatic dead letter queues and retry policies.

**Database:** Both services share the same PostgreSQL database. Each owns specific tables via `ExcludeFromMigrations()`:
- **Inference API** owns: `application_users`, `projects`, `project_files`, `agent_definitions` (history: `__EFMigrationsHistory_Api`)
- **WorkflowEngine** owns: `workflow_definitions`, `workflow_steps`, `workflow_executions`, `workflow_step_results`, `review_scores` (history: `__EFMigrationsHistory_Workflow`)
- **Startup order:** API migrates first (depends on postgres), Engine migrates second (depends on API healthy)

## Inference Service Structure

```
inference/
├── ReelForge.sln
├── src/
│   ├── ReelForge.Shared/                         # Shared class library
│   │   ├── Data/Models/                          # All EF Core entities + enums
│   │   ├── IntegrationEvents/                    # MassTransit message contracts
│   │   ├── Auth/ICurrentUser.cs                  # Interface only
│   │   └── SnakeCaseNamingHelper.cs              # Shared DB naming convention
│   │
│   ├── ReelForge.Inference.Api/                  # Service 1: REST API
│   │   ├── Controllers/                          # Projects, Files, Agents, Workflows CRUD
│   │   ├── Controllers/Dto/                      # Request/response DTOs
│   │   ├── Data/InferenceApiDbContext.cs          # Owns user/project/file/agent tables
│   │   ├── Data/DatabaseSeeder.cs                # Auto-migrate + seed agents
│   │   ├── Services/Auth/CurrentUser.cs          # JWT claims extraction
│   │   ├── Services/Storage/                     # MinIO/S3 file storage
│   │   ├── Services/Background/                  # File summarization queue
│   │   ├── Agents/                               # FileSummarizerAgent only
│   │   ├── Dockerfile
│   │   ├── Program.cs
│   │   └── appsettings.json
│   │
│   └── ReelForge.WorkflowEngine/                 # Service 2: Execution Engine
│       ├── Agents/                               # All 11 workflow agents
│       │   ├── Analysis/                         # 5 code analysis agents
│       │   ├── Translation/                      # Remotion + Animation agents
│       │   ├── Production/                       # Director, Scriptwriter, Author
│       │   ├── Quality/                          # ReviewAgent
│       │   └── Tools/                            # Shared AIFunction tools
│       ├── Consumers/                            # MassTransit consumer
│       ├── Execution/                            # Enhanced workflow executor
│       │   ├── WorkflowExecutorService.cs        # Step-executor strategy pattern
│       │   ├── IStepExecutor.cs                  # Strategy interface
│       │   ├── ExpressionEvaluator.cs            # NCalc condition evaluator
│       │   └── StepExecutors/                    # Agent, Conditional, ForEach, ReviewLoop
│       ├── Workers/WorkflowWorkerPool.cs         # Health monitoring service
│       ├── Controllers/                          # Health + admin endpoints
│       ├── Observability/ReelForgeDiagnostics.cs # OTel instrumentation
│       ├── Data/WorkflowEngineDbContext.cs        # Owns workflow tables
│       ├── Dockerfile
│       ├── Program.cs
│       └── appsettings.json
```

### Key Patterns

- **Agents** inherit from `ReelForgeAgentBase`, which wraps `IChatClient.AsAIAgent()`. Tools are registered via `AIFunctionFactory.Create()` and cast to `IList<AITool>`.
- **System prompts** are read from `appsettings.json` key `Agents:<AgentName>:SystemPrompt` with hardcoded fallback defaults.
- **MassTransit** handles RabbitMQ messaging. Inference API publishes `WorkflowExecutionRequested`, WorkflowEngine consumes it.
- **Step Executors** implement `IStepExecutor` strategy pattern: `AgentStepExecutor`, `ConditionalStepExecutor`, `ForEachStepExecutor`, `ReviewLoopStepExecutor`.
- **ExpressionEvaluator** uses NCalc for condition evaluation with JSON parameter extraction.
- **OpenTelemetry** provides distributed tracing and metrics via `ActivitySource` and `Meter`.
- **All controllers** require `[Authorize]` except `HealthController`. `ICurrentUser` extracts user identity from JWT claims.
- **Swagger** available in development at `/swagger` (both services).

### Enhanced Data Model

**New enums:** `StepType` (Agent, Conditional, ForEach, ReviewLoop), `StepStatus` (Pending, Running, Completed, Failed, Skipped)

**WorkflowStep** enhanced with: `StepType`, `ConditionExpression`, `LoopSourceExpression`, `LoopTargetStepOrder`, `MaxIterations`, `MinScore`, `InputMappingJson`, `TrueBranchStepOrder`, `FalseBranchStepOrder`

**WorkflowStepResult** enhanced with: `InputJson`, `OutputJson`, `Status` (StepStatus), `ErrorDetails`, `IterationNumber`, `CompletedAt`

**WorkflowExecution** enhanced with: `CorrelationId`, `InitiatedByUserId`, `ErrorMessage`

### Integration Events (MassTransit)

| Event | Publisher | Consumer |
|-------|-----------|----------|
| `WorkflowExecutionRequested` | Inference API | WorkflowEngine |
| `WorkflowExecutionCompleted` | WorkflowEngine | (available for consumers) |
| `WorkflowStepCompleted` | WorkflowEngine | (available for consumers) |
| `WorkflowExecutionFailed` | WorkflowEngine | (available for consumers) |

### Agent Types (enum)

Analysis: `CodeStructureAnalyzer`, `DependencyAnalyzer`, `ComponentInventoryAnalyzer`, `RouteAndApiAnalyzer`, `StyleAndThemeExtractor`
Translation: `RemotionComponentTranslator`, `AnimationStrategyAgent`
Production: `DirectorAgent`, `ScriptwriterAgent`, `AuthorAgent`
Quality: `ReviewAgent`
File Processing: `FileSummarizerAgent` (in Inference API only)
User-defined: `Custom`

### Default Workflow Pipeline

```
CodeStructureAnalyzer → DependencyAnalyzer → ComponentInventoryAnalyzer →
RouteAndApiAnalyzer → StyleAndThemeExtractor → RemotionComponentTranslator →
AnimationStrategy → Scriptwriter → Director → Author → Review
                                                ↑                    |
                                                └── (if score < 9) ─┘
```

### Frontend (Next.js)

**Framework:** Next.js 15 App Router + Mantine v8
**Data fetching:** SWR for server state (caching, dedup, revalidation)
**Auth:** httpOnly cookie managed by nginx. Frontend reads `reelforge_user` cookie (non-httpOnly) for UI state only.
**Icons:** `@tabler/icons-react`
**Drag-and-drop:** `@dnd-kit/sortable` for workflow step builder

#### Frontend Structure

```
web/
├── app/
│   ├── layout.tsx                     # MantineProvider, ColorSchemeScript, Notifications
│   ├── theme.ts                       # Mantine theme (violet primary)
│   ├── (auth)/                        # Public auth pages (centered card layout)
│   │   ├── login/page.tsx
│   │   └── change-password/page.tsx
│   ├── (app)/                         # Authenticated pages (AppShell: navbar + header)
│   │   ├── layout.tsx
│   │   ├── dashboard/page.tsx
│   │   ├── projects/
│   │   │   ├── page.tsx              # Project grid + create modal
│   │   │   └── [id]/
│   │   │       ├── page.tsx          # Tabbed: overview, files, workflows
│   │   │       └── workflows/
│   │   │           ├── new/page.tsx   # Workflow builder (drag-and-drop)
│   │   │           └── [workflowId]/
│   │   │               ├── page.tsx   # Workflow edit + execute
│   │   │               └── executions/[executionId]/page.tsx
│   │   ├── agents/
│   │   │   ├── page.tsx              # Grouped by category
│   │   │   └── [id]/page.tsx         # Read-only built-in, editable custom
│   │   └── admin/users/
│   │       ├── page.tsx              # User table + create
│   │       └── [id]/page.tsx         # User detail + edit
│   └── api/auth/me/route.ts          # Decode reelforge_user cookie for SSR
├── lib/
│   ├── api/                           # fetch wrappers (client.ts, auth.ts, projects.ts, etc.)
│   ├── hooks/                         # SWR hooks (use-auth.ts, use-projects.ts, etc.)
│   ├── types/                         # TypeScript interfaces (all camelCase)
│   └── utils/                         # constants.ts, format.ts
├── components/
│   ├── shell/                         # AppShell, NavLinks, UserMenu, ThemeToggle
│   ├── auth/                          # LoginForm, ChangePasswordForm
│   ├── projects/                      # ProjectCard, ProjectForm, StatusBadge
│   ├── files/                         # FileUploadZone, FileList, FileSummaryDrawer
│   ├── agents/                        # AgentCard, AgentForm, AgentTypeBadge
│   ├── workflows/                     # WorkflowStepList, StepCard, AgentPicker, ExecutionProgress
│   ├── admin/                         # UserTable, UserForm, TempPasswordModal
│   └── shared/                        # ConfirmModal, EmptyState, ErrorAlert, PageHeader
├── middleware.ts                      # Route protection (auth + admin guard)
├── Dockerfile                         # Multi-stage Next.js standalone build
└── package.json
```

#### Auth Flow (Frontend ↔ Nginx)

1. Login `POST /api/v1/auth/token` → nginx forwards to Go API → njs intercepts response, sets `reelforge_token` (httpOnly) + `reelforge_user` (readable) cookies
2. All `/api/v1/*` calls → nginx reads cookie, injects `Authorization: Bearer <token>` header
3. Logout `POST /api/auth/logout` → nginx clears cookies, returns 200
4. `middleware.ts` checks `reelforge_user` cookie: redirects unauthenticated to `/login`, `mustChangePassword` to `/change-password`, non-admin from `/admin/*` to `/dashboard`

### Nginx Reverse Proxy

**Config:** `nginx/nginx.conf` + `nginx/auth.js` (njs module)
**Single entry:** Port 80 (configurable via `APP_PORT` env var)

| Path | Upstream | Auth |
|------|----------|------|
| `/api/v1/auth/token` | `go-api:8080` | Login response intercepted for cookie setting |
| `/api/v1/auth/*` | `go-api:8080` | Cookie → Authorization header |
| `/api/v1/admin/*` | `go-api:8080` | Cookie → Authorization header |
| `/api/v1/workflow-engine/*` | `workflow-engine:8080` | Cookie → Authorization header |
| `/api/v1/*` | `inference:8080` | Cookie → Authorization header |
| `/health` | `go-api:8080` | None |
| `/api/auth/logout` | — | Nginx clears cookies |
| `/*` | `web:3000` | None (frontend) |

## Commands

### Go API (`/api`)

```bash
go run .                    # Run
go build -o reelforge .     # Build
go test ./...               # Test all
go test ./handlers/...      # Test single package
golangci-lint run           # Lint
go get <module> && go mod tidy  # Add dependency
```

### .NET Inference (from `/inference`)

```bash
dotnet build ReelForge.sln                          # Build entire solution
dotnet run --project src/ReelForge.Inference.Api     # Run API
dotnet run --project src/ReelForge.WorkflowEngine    # Run WorkflowEngine
dotnet test                                          # Test all
dotnet restore                                       # Restore packages

# EF Core migrations (Inference API context)
dotnet-ef migrations add <Name> --project src/ReelForge.Inference.Api --context InferenceApiDbContext
dotnet-ef database update --project src/ReelForge.Inference.Api --context InferenceApiDbContext

# EF Core migrations (WorkflowEngine context)
dotnet-ef migrations add <Name> --project src/ReelForge.WorkflowEngine --context WorkflowEngineDbContext
dotnet-ef database update --project src/ReelForge.WorkflowEngine --context WorkflowEngineDbContext
```

### Frontend (`/web`)

```bash
npm run dev                 # Dev server (Turbopack)
npm run build               # Production build
npm run start               # Start production server
npm run lint                # Lint
npm install <package>       # Add dependency
```

### Docker

All services have Dockerfiles and are orchestrated via `docker-compose.yml` at the repo root.

**Dockerfiles:**
- `api/Dockerfile` — multi-stage Go build (`golang:1.25-alpine` → `alpine:3.21`), injects version via `-ldflags`
- `inference/src/ReelForge.Inference.Api/Dockerfile` — multi-stage .NET build, references shared library
- `inference/src/ReelForge.WorkflowEngine/Dockerfile` — multi-stage .NET build, references shared library
- `web/Dockerfile` — multi-stage Next.js build (`node:22-alpine`, standalone output)

**Compose services:**

| Service | Image | Host Port | Internal Port | Notes |
|---------|-------|-----------|---------------|-------|
| `nginx` | `nginx:alpine` | 80 (`APP_PORT`) | 80 | Single entry point, njs cookie↔header translation |
| `web` | Built from `./web` | — (internal) | 3000 | Next.js frontend |
| `go-api` | Built from `./api` | — (internal) | 8080 | Depends on postgres (healthy) |
| `inference` | Built from `./inference` | — (internal) | 8080 | Inference API, depends on go-api + rabbitmq |
| `workflow-engine` | Built from `./inference` | — (internal) | 8080 | Workflow engine, depends on inference + rabbitmq |
| `postgres` | `postgres:16-alpine` | 5432 | 5432 | Volume `pgdata`, healthcheck via `pg_isready` |
| `minio` | `minio/minio:latest` | 9000/9001 | 9000/9001 | Volume `miniodata`, console on 9001 |
| `minio-init` | `minio/mc:latest` | — | — | One-shot: creates the `reelforge` bucket, then exits |
| `rabbitmq` | `rabbitmq:3-management-alpine` | 5672/15672 | 5672/15672 | Volume `rabbitmqdata`, management UI on 15672 |

```bash
docker compose up --build -d              # Start full stack
docker compose up --build -d <service>    # Rebuild single service
docker compose logs -f [service-name]     # View logs
docker compose down                       # Stop everything
docker compose down -v                    # Full reset
```

**Health endpoints (via nginx):**
- App: `http://localhost/health` (Go API health)
- Inference API: `http://localhost/api/v1/health`
- Workflow Engine: `http://localhost/api/v1/workflow-engine/health`
- Frontend: `http://localhost` (Next.js)
- MinIO Console: `http://localhost:9001` (direct)
- RabbitMQ Management: `http://localhost:15672` (direct)

## Configuration

All configuration is driven by `.env` at the repo root (copy `.env.example` to `.env`). The `.env` file is git-ignored.

### Environment Variables (`.env`)

| Variable | Default | Description |
|----------|---------|-------------|
| `POSTGRES_USER` | `postgres` | PostgreSQL username |
| `POSTGRES_PASSWORD` | `postgres` | PostgreSQL password |
| `POSTGRES_DB` | `reelforge` | PostgreSQL database name |
| `POSTGRES_PORT` | `5432` | PostgreSQL host port |
| `MINIO_ACCESS_KEY` | `minioadmin` | MinIO root user |
| `MINIO_SECRET_KEY` | `minioadmin` | MinIO root password |
| `MINIO_BUCKET` | `reelforge` | MinIO bucket name |
| `MINIO_API_PORT` | `9000` | MinIO API host port |
| `MINIO_CONSOLE_PORT` | `9001` | MinIO console host port |
| `JWT_SIGNING_KEY` | — | HMAC-SHA256 symmetric key (min 32 chars) |
| `JWT_ISSUER` | `reelforge-api` | JWT issuer claim |
| `JWT_AUDIENCE` | `reelforge-inference` | JWT audience claim |
| `AZURE_OPENAI_ENDPOINT` | — | Azure OpenAI endpoint URL |
| `AZURE_OPENAI_API_KEY` | — | Azure OpenAI API key |
| `AZURE_OPENAI_DEPLOYMENT` | `gpt-4o-mini` | Azure OpenAI deployment/model name |
| `APP_PORT` | `80` | Nginx reverse proxy host port |
| `ASPNETCORE_ENVIRONMENT` | `Development` | ASP.NET environment (`Development` / `Production`) |
| `RABBITMQ_USER` | `guest` | RabbitMQ username |
| `RABBITMQ_PASSWORD` | `guest` | RabbitMQ password |
| `RABBITMQ_PORT` | `5672` | RabbitMQ AMQP port |
| `RABBITMQ_MGMT_PORT` | `15672` | RabbitMQ management UI port |
| `WORKFLOW_MAX_CONCURRENCY` | `4` | Max parallel workflow executions |
| `SMTP_HOST` | — | SMTP server hostname (optional) |
| `SMTP_PORT` | `587` | SMTP server port |
| `SMTP_USERNAME` | — | SMTP auth username |
| `SMTP_PASSWORD` | — | SMTP auth password |
| `SMTP_FROM` | — | Sender email address |
| `ADMIN_EMAIL` | `admin@reelforge.local` | Initial admin user email |
| `ADMIN_PASSWORD` | — | Initial admin password (auto-generated if empty) |

### Inference `appsettings.json` Keys

Both services share these keys (overridden by Docker Compose env vars):

| Key | Description |
|-----|-------------|
| `ConnectionStrings:DefaultConnection` | PostgreSQL connection string |
| `Jwt:Issuer` / `Jwt:Audience` / `Jwt:SigningKey` | JWT validation (HS256) |
| `RabbitMQ:Host` / `RabbitMQ:Username` / `RabbitMQ:Password` | RabbitMQ connection |
| `AzureOpenAI:Endpoint` / `AzureOpenAI:ApiKey` / `AzureOpenAI:DeploymentName` | AI model backend |
| `MinIO:Endpoint` / `MinIO:AccessKey` / `MinIO:SecretKey` / `MinIO:BucketName` | S3-compatible storage (API only) |
| `WorkflowEngine:MaxConcurrency` | Max parallel executions (Engine only) |
| `Agents:<AgentName>:SystemPrompt` | Override any agent's system prompt |
