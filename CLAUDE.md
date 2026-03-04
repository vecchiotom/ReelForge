# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

ReelForge is a microservices platform for generating promotional videos using [Remotion](https://www.remotion.dev/) via agentic workflows. Three services coordinate to handle the frontend, API requests, and AI/agent inference:

- **`/web`** — Next.js 15 App Router + Mantine v8 frontend
- **`/api`** — Go REST API (Gorilla Mux, GORM, PostgreSQL)
- **`/inference`** — .NET 9 ASP.NET Core service running Microsoft Agent Framework workflows

All services are containerized and accessed through an nginx reverse proxy on a single port.

## Architecture

```
/nginx        Nginx reverse proxy — single entry point, cookie↔header JWT translation
/web          Next.js frontend — Mantine v8 UI, SWR data fetching
/api          Go REST API — user management, auth (JWT issuer), routes client requests
/inference    .NET agent orchestration — runs agentic workflows, calls Remotion for video rendering
```

Nginx is the single entry point (port 80). It routes requests to the appropriate backend and translates httpOnly cookies into Authorization headers. The frontend calls `/api/v1/*` on the same origin — no CORS needed. The Go API is the authority for user management and JWT issuance. It delegates AI/generation work to the inference service. The inference service uses Microsoft's Agent Framework to orchestrate multi-step workflows that ultimately produce Remotion-rendered promotional videos.

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
├── middleware/
│   ├── auth.go                   # Bearer token extraction, validation, UserContext injection
│   └── admin.go                  # IsAdmin check (403 if not admin)
├── handlers/
│   ├── handlers.go               # Route registration hub with subrouter middleware chaining
│   ├── health/health.go          # GET /health
│   ├── auth/
│   │   ├── auth.go               # POST /api/v1/auth/token, POST /api/v1/auth/change-password
│   │   └── dto.go                # TokenRequest, TokenResponse, ChangePasswordRequest
│   └── admin/
│       ├── users.go              # Admin user CRUD (POST/GET/PUT/DELETE /api/v1/admin/users)
│       └── dto.go                # CreateUserRequest/Response, UpdateUserRequest, UserResponse
├── main.go                       # Entry point: config load, DB init, admin seed, server start
├── Dockerfile                    # Multi-stage Go build
├── go.mod / go.sum
```

#### Go API Endpoints

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `GET` | `/health` | Public | Health check |
| `POST` | `/api/v1/auth/token` | Public | Login (email + password → JWT) |
| `POST` | `/api/v1/auth/change-password` | Authenticated | Change password (clears `must_change_password`) |
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
5. JWT tokens (24h expiry) are used for all authenticated endpoints in both Go API and inference service

#### Middleware Chain

- **Public routes:** `/health`, `/api/v1/auth/token` — no middleware
- **Authenticated routes:** `/api/v1/auth/change-password` — `middleware.Auth` (validates JWT, injects `UserContext`)
- **Admin routes:** `/api/v1/admin/*` — `middleware.Auth` → `middleware.Admin` (checks `IsAdmin`)

### Inference Service

**Framework:** ASP.NET Core 9.0 + Microsoft Agent Framework (`Microsoft.Agents.AI` 1.0.0-rc2)
**Database:** PostgreSQL via EF Core (Npgsql) — snake_case naming convention
**Auth:** JWT Bearer tokens issued by Go API — validated via `Microsoft.AspNetCore.Authentication.JwtBearer`. `ICurrentUser` extracts `sub`, `email`, and `isAdmin` claims (camelCase).
**Storage:** MinIO (S3-compatible) via `AWSSDK.S3`
**AI Backend:** Azure OpenAI via `Azure.AI.OpenAI` — configurable endpoint/key/deployment in `appsettings.json`

**Inference service ports:**
- Local dev: HTTP `5200`, HTTPS `7123`
- Docker: HTTP `8080`, HTTPS `8081`

## Inference Service Structure

```
inference/ReelForge.Inference/
├── Agents/                        # Agent system
│   ├── IReelForgeAgent.cs         # Agent interface
│   ├── ReelForgeAgentBase.cs      # Abstract base (uses IChatClient.AsAIAgent())
│   ├── IAgentRegistry.cs          # Registry interface
│   ├── AgentRegistry.cs           # Resolves agents by AgentType enum
│   ├── Tools/                     # Shared AIFunction tools for agents
│   ├── Analysis/                  # 5 code analysis agents
│   ├── Translation/               # RemotionComponentTranslator, AnimationStrategy
│   ├── Production/                # Director, Scriptwriter, Author
│   ├── Quality/                   # ReviewAgent (scores 1-10)
│   └── FileProcessing/            # FileSummarizerAgent
├── Controllers/                   # REST API (all under /api/v1/)
│   ├── Dto/                       # Request/response DTOs
│   ├── ProjectsController.cs      # CRUD for projects
│   ├── ProjectFilesController.cs  # File upload/delete with async summarization
│   ├── AgentsController.cs        # Agent definition CRUD
│   ├── WorkflowsController.cs     # Workflow CRUD + execute + poll status
│   └── HealthController.cs        # Anonymous /health endpoint
├── Data/
│   ├── Models/                    # EF Core entities (9 models + enums)
│   ├── ReelForgeDbContext.cs      # DbContext with snake_case convention
│   └── DatabaseSeeder.cs          # Auto-migrate + seed built-in agents
├── Services/
│   ├── Auth/                      # ICurrentUser — extracts JWT sub/email/is_admin claims
│   ├── Storage/                   # IFileStorageService — S3/MinIO abstraction
│   └── Background/                # Channel-based task queues + hosted services
├── Workflows/
│   └── WorkflowExecutorService.cs # Dynamic workflow execution from DB definitions
├── Migrations/                    # EF Core migrations
├── Program.cs                     # Full DI wiring
└── appsettings.json               # Config: DB, JWT, MinIO, Azure OpenAI
```

### Key Patterns

- **Agents** inherit from `ReelForgeAgentBase`, which wraps `IChatClient.AsAIAgent()`. Tools are registered via `AIFunctionFactory.Create()` and cast to `IList<AITool>`.
- **System prompts** are read from `appsettings.json` key `Agents:<AgentName>:SystemPrompt` with hardcoded fallback defaults.
- **Workflows** are dynamically constructed from `WorkflowDefinition` + `WorkflowStep` DB records. The `WorkflowExecutorService` iterates steps sequentially, handles the review loop (score < 9 → retry from Director, up to 3 iterations).
- **Background processing** uses `System.Threading.Channels` via `IBackgroundTaskQueue<T>` — designed to be swappable for a Service Bus queue later.
- **All controllers** require `[Authorize]` except `HealthController`. The `ICurrentUser` service extracts user identity (`UserId`, `Email`, `IsAdmin`) from JWT claims.
- **Swagger** is available in development at `/swagger` with Bearer auth support (Swashbuckle 6.9.0).

### Data Model

`ApplicationUser` (+ `PasswordHash`, `IsAdmin`, `MustChangePassword`) → `Project` → `ProjectFile` (MinIO storage)
`Project` → `WorkflowDefinition` → `WorkflowStep` (ordered, linked to `AgentDefinition`)
`WorkflowDefinition` → `WorkflowExecution` → `WorkflowStepResult`, `ReviewScore`
`AgentDefinition` — built-in (seeded) or custom (user-created, `AgentType.Custom`)

### Agent Types (enum)

Analysis: `CodeStructureAnalyzer`, `DependencyAnalyzer`, `ComponentInventoryAnalyzer`, `RouteAndApiAnalyzer`, `StyleAndThemeExtractor`
Translation: `RemotionComponentTranslator`, `AnimationStrategyAgent`
Production: `DirectorAgent`, `ScriptwriterAgent`, `AuthorAgent`
Quality: `ReviewAgent`
File Processing: `FileSummarizerAgent`
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

### .NET Inference (`/inference/ReelForge.Inference`)

```bash
dotnet run                  # Run
dotnet build                # Build
dotnet test                 # Test
dotnet test --filter "FullyQualifiedName~TestName"  # Single test
dotnet restore              # Restore packages
dotnet publish -c Release   # Publish

# EF Core migrations
dotnet-ef migrations add <Name>   # Create migration
dotnet-ef database update         # Apply migrations
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

All three services have Dockerfiles and are orchestrated via `docker-compose.yml` at the repo root.

**Dockerfiles:**
- `api/Dockerfile` — multi-stage Go build (`golang:1.25-alpine` → `alpine:3.21`), injects version via `-ldflags`
- `inference/ReelForge.Inference/Dockerfile` — multi-stage .NET build (`dotnet/sdk:9.0` → `dotnet/aspnet:9.0`)
- `web/Dockerfile` — multi-stage Next.js build (`node:22-alpine`, standalone output)

**Compose services:**

| Service | Image | Host Port | Internal Port | Notes |
|---------|-------|-----------|---------------|-------|
| `nginx` | `nginx:alpine` | 80 (`APP_PORT`) | 80 | Single entry point, njs cookie↔header translation |
| `web` | Built from `./web` | — (internal) | 3000 | Next.js frontend |
| `go-api` | Built from `./api` | — (internal) | 8080 | Depends on postgres (healthy) |
| `inference` | Built from `./inference/ReelForge.Inference` | — (internal) | 8080 | Depends on postgres + minio-init |
| `postgres` | `postgres:16-alpine` | 5432 | 5432 | Volume `pgdata`, healthcheck via `pg_isready` |
| `minio` | `minio/minio:latest` | 9000/9001 | 9000/9001 | Volume `miniodata`, console on 9001 |
| `minio-init` | `minio/mc:latest` | — | — | One-shot: creates the `reelforge` bucket, then exits |

All services share a `reelforge` bridge network and reference each other by service name. Nginx is the only externally-exposed service — go-api and inference are internal-only. Inference env vars use ASP.NET `__` separator convention to override `appsettings.json` sections.

```bash
# Start the full stack (builds images if needed)
docker compose up --build -d

# Rebuild and restart a single service after code changes
docker compose up --build -d <service-name>

# View logs
docker compose logs -f [service-name]

# Stop everything
docker compose down

# Stop and remove volumes (full reset)
docker compose down -v
```

**Health endpoints (via nginx):**
- App: `http://localhost/health` (Go API health)
- Frontend: `http://localhost` (Next.js)
- MinIO Console: `http://localhost:9001` (direct)

## Configuration

All configuration is driven by `.env` at the repo root (copy `.env.example` to `.env`). The `.env` file is git-ignored.

**Dev vs Production** is controlled entirely by `.env` values — no separate compose files needed. Set `ASPNETCORE_ENVIRONMENT=Development` for Swagger, `Production` for prod. For future dev-specific overrides (hot reload, debug ports), use `docker-compose.override.yml`.

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
| `APP_PORT` | `80` | Nginx reverse proxy host port (single entry point) |
| `API_PORT` | `3000` | Go API host port (legacy, not used with nginx) |
| `INFERENCE_PORT` | `3001` | Inference service host port (legacy, not used with nginx) |
| `ASPNETCORE_ENVIRONMENT` | `Development` | ASP.NET environment (`Development` / `Production`) |
| `SMTP_HOST` | — | SMTP server hostname (optional — OTP logged to console if not set) |
| `SMTP_PORT` | `587` | SMTP server port |
| `SMTP_USERNAME` | — | SMTP auth username |
| `SMTP_PASSWORD` | — | SMTP auth password |
| `SMTP_FROM` | — | Sender email address |
| `ADMIN_EMAIL` | `admin@reelforge.local` | Initial admin user email (seeded on first startup) |
| `ADMIN_PASSWORD` | — | Initial admin password (auto-generated if empty) |

### Inference `appsettings.json` Keys

These are overridden by Docker Compose env vars in container deployments:

| Key | Description |
|-----|-------------|
| `ConnectionStrings:DefaultConnection` | PostgreSQL connection string |
| `Jwt:Issuer` / `Jwt:Audience` / `Jwt:SigningKey` | JWT validation (HS256, `SymmetricSecurityKey`) |
| `MinIO:Endpoint` / `MinIO:AccessKey` / `MinIO:SecretKey` / `MinIO:BucketName` | S3-compatible storage |
| `AzureOpenAI:Endpoint` / `AzureOpenAI:ApiKey` / `AzureOpenAI:DeploymentName` | AI model backend |
| `Agents:<AgentName>:SystemPrompt` | Override any agent's system prompt |
