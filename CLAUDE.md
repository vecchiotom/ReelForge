# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

ReelForge is a microservices platform for generating promotional videos using [Remotion](https://www.remotion.dev/) via agentic workflows. Four services coordinate to handle the frontend, API requests, and AI/agent inference:

- **`/site`** — Next.js 15 App Router + Tailwind CSS v4 public marketing site, served at the root domain (`/`)
- **`/web`** — Next.js 15 App Router + Mantine v8 authenticated platform dashboard, served under `/app`
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

See [`docs/marketing-site.md`](docs/marketing-site.md) for the public marketing site's routing split (`/` → `site`, `/app/*` → `web`) and its launch checklist. `/site` also ships a WebGL/three.js hero (homepage-only, lazy-loaded behind `next/dynamic`) and a Chakra Petch/JetBrains Mono type system — see [`docs/site-design-system.md`](docs/site-design-system.md).

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
- **Inference API** owns: `application_users`, `projects`, `project_files`, `agent_definitions`, `inference_providers` (history: `__EFMigrationsHistory_Api`)
- **WorkflowEngine** owns: `workflow_definitions`, `workflow_steps`, `workflow_executions`, `workflow_step_results`, `review_scores` (history: `__EFMigrationsHistory_Workflow`)
- **WorkflowEngine** also maps `inference_providers` **read-only** (`ExcludeFromMigrations()`), since agent chat-client resolution reads provider config directly from its own DbContext.
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
│   │   ├── Workflows/ExtractStepConfig.cs        # Typed config for StepType.Extract (+ WorkflowTemplateCatalog)
│   │   ├── Inference/                            # Provider-agnostic chat-client abstraction (factory, resolver,
│   │   │                                          # IAgentChatClientProvider, ISecretProtector) shared by both services
│   │   └── SnakeCaseNamingHelper.cs              # Shared DB naming convention
│   │
│   ├── ReelForge.Inference.Api/                  # Service 1: REST API
│   │   ├── Controllers/                          # Projects, Files, Agents, Workflows, InferenceProviders, Outputs,
│   │   │                                          # StepResultArtifacts CRUD
│   │   ├── Controllers/Dto/                      # Request/response DTOs
│   │   ├── Data/InferenceApiDbContext.cs          # Owns user/project/file/agent/inference-provider tables
│   │   ├── Data/DatabaseSeeder.cs                # Auto-migrate + seed agents
│   │   ├── Services/Auth/CurrentUser.cs          # JWT claims extraction
│   │   ├── Services/Storage/                     # MinIO/S3 file storage
│   │   ├── Services/Background/                  # File summarization queue
│   │   ├── Services/VectorSearch/                # Qdrant chunking/embedding + semantic file search
│   │   ├── Services/Inference/                   # InferenceApiProviderStore (IInferenceProviderStore impl)
│   │   ├── Agents/                               # FileSummarizerAgent only
│   │   ├── Dockerfile
│   │   ├── Program.cs
│   │   └── appsettings.json
│   │
│   └── ReelForge.WorkflowEngine/                 # Service 2: Execution Engine
│       ├── Agents/                               # All 17 workflow agents (11 original + VideoStoryEditor + MotionGraphicsPlanner + VideoReviewAgent + MusicSupervisor + VideoEditDirector + MotionGraphicsDirector)
│       │   ├── Analysis/                         # 5 code analysis agents
│       │   ├── Translation/                      # Remotion + Animation agents
│       │   ├── Production/                       # Director, Scriptwriter, Author, VideoStoryEditor, MotionGraphicsPlanner, MusicSupervisor
│       │   ├── Quality/                          # ReviewAgent, VideoReviewAgent
│       │   └── Tools/                            # Shared AIFunction tools
│       ├── Consumers/                            # MassTransit consumer
│       ├── Execution/                            # Enhanced workflow executor
│       │   ├── WorkflowExecutorService.cs        # Step-executor strategy pattern
│       │   ├── IStepExecutor.cs                  # Strategy interface
│       │   ├── ExpressionEvaluator.cs            # NCalc condition evaluator
│       │   └── StepExecutors/                    # Agent, Conditional, ForEach, ReviewLoop, Parallel, Extract,
│       │                                          # VideoAnalyze, VideoCompile
│       ├── Services/Inference/                   # WorkflowEngineProviderStore (IInferenceProviderStore impl)
│       ├── Services/Video/                       # ffmpeg/ffprobe runner, silence/shot detectors, audio extractor,
│       │                                          # scratch-space management (see docs/video-editing.md)
│       ├── Workers/WorkflowWorkerPool.cs         # Health monitoring service
│       ├── Controllers/                          # Health + admin endpoints
│       ├── Observability/ReelForgeDiagnostics.cs # OTel instrumentation
│       ├── Data/WorkflowEngineDbContext.cs        # Owns workflow tables
│       ├── Dockerfile
│       ├── Program.cs
│       └── appsettings.json
│
├── tests/
│   └── ReelForge.WorkflowEngine.Tests/           # xUnit + FluentAssertions + Moq + EFCore.InMemory
```

### Key Patterns

- **Agents** inherit from `ReelForgeAgentBase`, which wraps `IChatClient.AsAIAgent()`. Tools are registered via `AIFunctionFactory.Create()` and cast to `IList<AITool>`.
- **System prompts** are read from `appsettings.json` key `Agents:<AgentName>:SystemPrompt` with hardcoded fallback defaults.
- **MassTransit** handles RabbitMQ messaging. Inference API publishes `WorkflowExecutionRequested`, WorkflowEngine consumes it.
- **Step Executors** implement `IStepExecutor` strategy pattern: `AgentStepExecutor`, `ConditionalStepExecutor`, `ForEachStepExecutor`, `ReviewLoopStepExecutor`, `ParallelStepExecutor`, `ExtractStepExecutor`, `VideoAnalyzeStepExecutor`, `VideoCompileStepExecutor`, and the two room executors `EditRoomStepExecutor`/`GraphicsRoomStepExecutor` (both thin bindings of the shared `RoomStepExecutorBase<TDecision>` — see `docs/video-editing.md` "The shared room infrastructure") (all four non-`Agent` deterministic types are non-LLM — no `IChatClient`/`IAgentRegistry` dependency, always retried at most once; the video pair also never throws, always emitting valid JSON even on failure, since `output_json` is `jsonb`).
- **Inference providers** are resolved per agent via `IAgentChatClientProvider` → `IInferenceProviderResolver` (60s TTL-cached, `Inference:ProviderCacheSeconds`) → `IChatClientFactory`. Chat precedence: per-agent `AgentDefinition.InferenceProviderId` override → the single `inference_providers` row with `IsDefault = true AND Capability = Chat` → the legacy `AzureOpenAI:*` config keys as a final fallback. Transcription precedence (used by `VideoAnalyze` steps via `ITranscriptionClientFactory`): `VideoAnalyzeStepConfig.TranscriptionProviderId` → the single row with `IsDefault = true AND Capability = Transcription` → none (no legacy config fallback — silently sending audio to a chat deployment would 404 confusingly). Vision precedence (Phase 2 of video editing, `VideoAnalyze` shot captioning via `IShotCaptioner`/reused `IChatClientFactory`): `VideoAnalyzeStepConfig.VisionProviderId` → the single row with `IsDefault = true AND Capability = Vision` → none (same no-legacy-fallback rationale as transcription). All three capabilities' defaults are fully independent (composite unique index), and each resolution path filters on its own `Capability` explicitly rather than picking "any `IsDefault` row". API keys are encrypted at rest with ASP.NET Core Data Protection (`ISecretProtector`), keyed on a shared `dpkeys` volume mounted at `/keys` in both services so either can decrypt what the other wrote.
- **ExpressionEvaluator** uses NCalc for condition evaluation with JSON parameter extraction.
- **OpenTelemetry** provides distributed tracing and metrics via `ActivitySource` and `Meter`.
- **All controllers** require `[Authorize]` except `HealthController`. `ICurrentUser` extracts user identity from JWT claims.
- **Swagger** available in development at `/swagger` (both services).

### Enhanced Data Model

**New enums:** `StepType` (Agent, Conditional, ForEach, ReviewLoop, Parallel, Extract, **VideoAnalyze**, **VideoCompile**, **EditRoom**, **GraphicsRoom**), `StepStatus` (Pending, Running, Completed, Failed, Skipped), `InferenceProviderKind` (AzureOpenAI, OpenAICompatible), **`InferenceProviderCapability`** (**Chat**, **Transcription**, **Vision** — what a provider row can be used for; see below)

**WorkflowStep** enhanced with: `StepType`, `ConditionExpression`, `LoopSourceExpression`, `LoopTargetStepOrder`, `MaxIterations`, `MinScore`, `InputMappingJson`, `TrueBranchStepOrder`, `FalseBranchStepOrder`, `ParallelAgentIdsJson` (`StepType.Parallel` — JSON array of agent GUIDs run concurrently), `ExtractConfigJson` (`extract_config_json` column, `StepType.Extract` — JSON-serialized `ExtractStepConfig`: closed to three operations, `Project`/`Resolve`/`Files`, always emitting a `{view, meta}` envelope), **`VideoAnalyzeConfigJson`** (`video_analyze_config_json`, jsonb, `StepType.VideoAnalyze` — JSON-serialized `VideoAnalyzeStepConfig`), **`VideoCompileConfigJson`** (`video_compile_config_json`, jsonb, `StepType.VideoCompile` — JSON-serialized `VideoCompileStepConfig`), **`EditRoomConfigJson`** (`edit_room_config_json`, jsonb, `StepType.EditRoom` — JSON-serialized `EditRoomStepConfig`), **`GraphicsRoomConfigJson`** (`graphics_room_config_json`, jsonb, `StepType.GraphicsRoom` — JSON-serialized `GraphicsRoomStepConfig`)

**WorkflowStepResult** enhanced with: `InputJson`, `OutputJson`, `Status` (StepStatus), `ErrorDetails`, `IterationNumber`, `CompletedAt`, **`ArtifactStorageKey`** (`artifact_storage_key`, text, nullable — storage key of a large non-playable artifact such as a `VideoAnalyze` full analysis JSON or a `VideoCompile` EDL; kept as a column **separate** from `OutputStorageKey` so `OutputsController`/the execution UI never mistakes a JSON artifact for a playable render — see [`docs/video-editing.md`](docs/video-editing.md))

**WorkflowExecution** enhanced with: `CorrelationId`, `InitiatedByUserId`, `ErrorMessage`

**AgentDefinition** enhanced with: `InferenceProviderId` (nullable FK to `inference_providers`, `OnDelete(SetNull)`) + `InferenceProviderName` (denormalized on the response DTO only) — the per-agent inference-provider override.

**InferenceProvider** (new entity, table `inference_providers`): `Id`, `Name` (unique), `Kind` (`InferenceProviderKind`), **`Capability`** (`InferenceProviderCapability`, default `Chat`), `Endpoint`, `ModelName`, `ApiKeyEncrypted`/`ApiKeyLastFour` (never returned in plaintext), `IsDefault` (**composite unique partial index on `(capability, is_default)` where `is_default`** — at most one default row *per capability*, so a Transcription default, a Vision default, and a Chat default all coexist independently; the index is generic over any `capability` value, so adding `Vision` needed no schema migration), `IsEnabled`, `TimeoutSeconds`, `ExtraHeadersJson`, `LastTestAt`/`LastTestOk`/`LastTestError`. Chat-completion resolution (`IInferenceProviderResolver.ResolveAsync`), transcription resolution (`ResolveTranscriptionAsync`), and vision resolution (`ResolveVisionAsync`) each filter on their own `Capability` explicitly — never "any `IsDefault` row" — since the three default rows are independent.

### Integration Events (MassTransit)

| Event | Publisher | Consumer |
|-------|-----------|----------|
| `WorkflowExecutionRequested` | Inference API | WorkflowEngine |
| `WorkflowExecutionCompleted` | WorkflowEngine | (available for consumers) |
| `WorkflowStepCompleted` | WorkflowEngine | (available for consumers) |
| `WorkflowExecutionFailed` | WorkflowEngine | (available for consumers) |
| `WorkflowStepProgress` | WorkflowEngine | Go API (relayed via `GET /api/v1/workflows/events` SSE as `step.progress`) — ephemeral UI progress signal (e.g. "Downloading source", "Encoding" with a percent) for a long-running step; never persisted to `WorkflowStepResult`, never authoritative — `WorkflowStepCompleted`/`WorkflowExecutionFailed` remain the only signal that a step is actually done |
| `WorkflowStepChatTurn` | WorkflowEngine | (available for consumers — Go API/frontend relay is a separate, follow-up pass) — append-only, sequence-numbered event published once per completed turn in a room step's (`StepType.EditRoom`/`GraphicsRoom`) group-chat run (structural twin of `WorkflowStepReasoningCaptured`, NOT a reuse of the ephemeral/supersedable `WorkflowStepProgress`); free-form model prose, never authoritative — see `docs/video-editing.md` "The edit room" |

### Inference Provider Endpoints (Inference API)

Admin-only CRUD for configured chat-completion providers (Azure OpenAI or an OpenAI-compatible
endpoint such as vLLM), plus the per-agent override. Deliberately routed under
`/api/v1/inference-providers` rather than `/api/v1/admin/*`, since nginx routes `/api/v1/admin/*`
to the Go API — see the Nginx table below.

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `GET` | `/api/v1/inference-providers` | Admin | List providers (secrets redacted: `hasApiKey`/`apiKeyLastFour` only) |
| `GET` | `/api/v1/inference-providers/{id}` | Admin | Get a single provider |
| `POST` | `/api/v1/inference-providers` | Admin | Create a provider; `isDefault: true` clears it on all others in the same transaction |
| `PUT` | `/api/v1/inference-providers/{id}` | Admin | Update; `apiKey` omitted/null leaves the stored key unchanged, `""` clears it |
| `DELETE` | `/api/v1/inference-providers/{id}` | Admin | `409 Conflict` if the provider `IsDefault`; referencing agents fall back to the default (`OnDelete(SetNull)`) |
| `POST` | `/api/v1/inference-providers/{id}/test` | Admin | 1-token ping through a saved provider; persists `LastTestAt`/`LastTestOk`/`LastTestError` |
| `POST` | `/api/v1/inference-providers/test` | Admin | Tests an unsaved config; reuses the stored key when `id` is supplied and `apiKey` is omitted |
| `PUT` | `/api/v1/agents/{id}/inference-provider` | Admin | Set/clear the per-agent provider override (`{ "inferenceProviderId": "<guid>" \| null }`) — unlike `PUT /api/v1/agents/{id}`, this is allowed for built-in agents, since overriding a built-in's provider is the primary use case |

Every create/update/test request above also accepts a `capability` field (`"Chat"`, `"Transcription"`,
or `"Vision"` — defaults to `"Chat"` when omitted for backward compatibility). `POST /{id}/test` and
`POST /test` branch on it: `Chat` runs the existing 1-token chat ping, `Transcription` transcribes a
~0.3s in-memory-synthesized silent WAV through `ITranscriptionClient`, `Vision` (Phase 2 of video
editing — shot captioning, see `docs/video-editing.md`) sends a trivial embedded 1x1 JPEG through
`IChatClientFactory` with a "reply ok" prompt. The admin UI (`InferenceProviderForm`) exposes
Capability as a field on create/edit, and the per-agent provider override picker
(`AgentInferenceProviderSelect`) filters to `Chat` rows only (an allowlist — "not Transcription"
alone would have silently admitted `Vision` rows too), since that override feeds chat resolution only.

### Video Editing Endpoints (Inference API)

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `GET` | `/api/v1/projects/{projectId}/step-results/{stepResultId}/artifact` | Owner | Streams the large JSON artifact (`WorkflowStepResult.ArtifactStorageKey`) for a `VideoAnalyze`/`VideoCompile` step result — the full analysis document or the compile EDL. Validates the storage key against the `projects/{projectId}/agentFiles/video-analysis` prefix (never `outputFiles`) so it can't be coerced into serving a playable render or another project's object. |

See [`docs/video-editing.md`](docs/video-editing.md) for the full feature: the three-stage
`VideoAnalyze` → `Agent(VideoStoryEditor)` → `VideoCompile` pipeline, the id-anchored no-timestamp
decision contract, artifact layout, and config reference.

### Agent Types (enum)

Analysis: `CodeStructureAnalyzer`, `DependencyAnalyzer`, `ComponentInventoryAnalyzer`, `RouteAndApiAnalyzer`, `StyleAndThemeExtractor`
Translation: `RemotionComponentTranslator`, `AnimationStrategyAgent`
Production: `DirectorAgent`, `ScriptwriterAgent`, `AuthorAgent`
Quality: `ReviewAgent`
File Processing: `FileSummarizerAgent` (in Inference API only)
Extract/Transform: `ExtractTransform` — built-in, non-LLM agent row seeded so `StepType.Extract` steps satisfy the non-nullable `WorkflowStep.AgentDefinitionId` FK; `SystemPrompt` is empty and `GeneratesOutput` is `false` since it is never sent to a model, only run as deterministic code by `ExtractStepExecutor`.
Video editing: `VideoStoryEditor` — LLM agent, `OutputSchemaName = "VideoEditDecisionOutput"`; decides which shots/silence-gaps/transcript-spans to KEEP from a bounded, id-anchored view produced by a `VideoAnalyze` step. Structurally incapable of emitting a timestamp (guarded by a reflection test, `VideoEditDecisionOutputInvariantTests`) — see [`docs/video-editing.md`](docs/video-editing.md). Tool access is read-only project context + `FailWorkflow`; no sandbox tools, no write/render tools. `MotionGraphicsPlanner` — LLM agent (Phase 3), `OutputSchemaName = "MotionGraphicsPlanOutput"`; plans zero or more motion-graphics overlays (lower-thirds, titles, callouts) anchored only to opaque placement ids offered by a `VideoAnalyze` step's `view.placements`. Structurally incapable of emitting a timestamp OR a pixel coordinate (guarded by `MotionGraphicsPlanOutputInvariantTests`). Unlike `VideoStoryEditor`/`VideoReviewAgent`/`MusicSupervisor` below, this agent is now granted the **same full sandbox+Remotion+render tool set as `AuthorAgent`, minus `WriteProjectFile`** (`AgentToolProvider`), so it can optionally back an overlay with a real, designed/animated Remotion-rendered transparent asset (`MotionGraphicsOverlay.RenderedAssetStorageKey`) instead of only a plain drawtext/drawbox overlay — see [`docs/video-editing.md`](docs/video-editing.md) "Motion graphics (Phase 3)". Phase 5 additionally lets this same agent plan zero or more tracked screen inserts (`MotionGraphicsPlanOutput.Inserts` — `ScreenInsert` is three strings: an offered `r{n}` region id from `view.insertRegions`, a self-rendered opaque asset key, prose) that `VideoCompileStepExecutor` corner-pins into a `ChromaQuadTracker`-tracked chroma-plate region — see [`docs/video-editing.md`](docs/video-editing.md) "Tracked screen inserts (Phase 5)". **A conscious tradeoff, not an oversight:** this is the first agent in the video-editing feature whose prompt includes analysis-view content *derived from the source video itself* (on-screen text the Phase 2 vision model read, ASR transcript text) rather than only user-selected project files, and the first video-editing agent with code-execution tools — a prompt injection hidden in that media-derived content could in principle reach the sandbox. The sandbox's existing containment (read-only rootfs, no network egress by default, no Docker-socket access — see [`docs/video-editing.md`](docs/video-editing.md) "Security: why ffmpeg is not in the sandbox" and `docs/sandbox-service.md`) is what bounds the blast radius here: worst case is sandbox-contained code execution, not host compromise. Accepted deliberately for this one agent, the same way the ffmpeg-vs-sandbox placement decision above is — not a gap nobody noticed. `VideoReviewAgent` — LLM agent, `OutputSchemaName = "VideoReviewOutput"`; used by a `StepType.ReviewLoop` step in the video-editing templates to score a compiled edit against deterministic facts `VideoCompileStepExecutor` already computed (transcript sentence-boundary check, overlay frame coverage, `music.dialogueHeadroom` when background music is enabled) rather than judging code/lint quality like `ReviewAgent`. Same minimal read-only tool scope as `VideoStoryEditor`. `MusicSupervisor` — LLM agent, `OutputSchemaName = "MusicPlanOutput"`; picks at most one background-music track (an offered `m{n}` id drawn from a `VideoAnalyze` step's `OfferMusicTracks`-derived candidate list) plus enum-word `Intensity`/`Ducking`/`Fit` settings for the pipeline's optional background music — see [`docs/video-editing.md`](docs/video-editing.md) "Background music". Never emits a dB value, a level, or a timestamp; same minimal read-only tool scope as `VideoStoryEditor`. Entirely optional — the deterministic `VideoCompileStepConfig.MusicTrackProjectFileId` path delivers the whole capability without this agent. `VideoTransform` — deterministic, non-LLM placeholder agent (identical role to `ExtractTransform`) seeded so `StepType.VideoAnalyze`/`VideoCompile`/`EditRoom`/`GraphicsRoom` steps satisfy the same non-nullable FK; runs ffmpeg, never a model (a room step's real seats/director are resolved independently, from `EditRoomConfigJson`/`GraphicsRoomConfigJson`, never from the step's own `AgentDefinitionId`). `VideoEditDirector` — LLM agent, `OutputSchemaName = "VideoEditDecisionOutput"` (reused verbatim from `VideoStoryEditor` — same rushcut invariant); used TWICE by a `StepType.EditRoom` step: once per-turn as the moderator participant in the room's live `Microsoft.Agents.AI.Workflows` group chat (free-form prose, emits the literal sentinel `ROOM_DECIDED` once satisfied), and once more, OUTSIDE the group chat, for a single ordinary structured-output synthesis call that converts the room's discussion into the final decision. Same minimal read-only tool scope as `VideoStoryEditor`. `MotionGraphicsDirector` — LLM agent, `OutputSchemaName = "MotionGraphicsPlanOutput"` (reused verbatim from `MotionGraphicsPlanner` — same no-timestamp/no-coordinate invariant, `MotionGraphicsPlanOutputInvariantTests` unchanged); the graphics-room analogue of `VideoEditDirector`, used TWICE by a `StepType.GraphicsRoom` step (room-participant moderator turns + one standalone synthesis call). UNLIKE `VideoEditDirector`, its tool grant is the same full sandbox+Remotion+render set as `MotionGraphicsPlanner` (minus `WriteProjectFile`) so the synthesis call can back an overlay with a real rendered asset — but room-participant turns are tool-restricted to read-only by `GraphicsRoomStepExecutor.GetRoomTurnTools`, so sandbox tools are reachable only from the synthesis call. The `MotionGraphicsPlanner` prompt-injection tradeoff applies identically and is accepted for the same reasons — see `docs/video-editing.md` "The graphics room".
User-defined: `Custom`

### Default Workflow Pipeline

```
CodeStructureAnalyzer → DependencyAnalyzer → ComponentInventoryAnalyzer →
RouteAndApiAnalyzer → StyleAndThemeExtractor → RemotionComponentTranslator →
AnimationStrategy → Scriptwriter → Director → Author → Review
                                                ↑                    |
                                                └── (if score < 9) ─┘
```

The `quick-win-promo` template above (`AutoCreateOnProject: true`) is unmodified. A second,
opt-in template — `lean-context-promo` (`AutoCreateOnProject: false`) — inserts a `StepType.Extract`
step (op `Project`) after `ComponentInventoryAnalyzer` to reduce its output to a bounded
`{view, meta}` view before it reaches the translation/production agents, cutting token usage.

A third, opt-in template — `video-derush-edit` (`AutoCreateOnProject: false`) — demonstrates the
video-editing feature end to end: `VideoAnalyze` (`Source: PreviousStepOutput`) → `Agent(VideoStoryEditor)`
→ `VideoCompile` (`Decision: Previous`, `AnalysisStepOrder: 1`). A fourth, opt-in template —
`video-derush-edit-graphics` (`AutoCreateOnProject: false`) — extends that pipeline with Phase 3
motion graphics: `VideoAnalyze` (`emitOverlayPlacements: true`) → `Agent(VideoStoryEditor)` →
`Agent(MotionGraphicsPlanner)` → `VideoCompile` (`enableGraphics: true`, `graphicsPlan` pointing at
the `MotionGraphicsPlanner` step). A fifth, opt-in template — `video-derush-edit-music`
(`AutoCreateOnProject: false`) — extends `video-derush-edit` with background music instead of
graphics: `VideoAnalyze` (`offerMusicTracks: true`) → `Agent(VideoStoryEditor)` →
`Agent(MusicSupervisor)` → `VideoCompile` (`enableMusic: true`, `musicPlan` pointing at the
`MusicSupervisor` step). Each of `Decision`/`GraphicsPlan`/`MusicPlan` on the compile step
references its source step explicitly by `StepOrder` rather than `Previous`, since `Previous`
relative to the compile step would resolve to the `MotionGraphicsPlanner`/`MusicSupervisor` step's
own output, not the story editor's decision. See [`docs/video-editing.md`](docs/video-editing.md).
A sixth, opt-in template — `video-derush-edit-room` (`AutoCreateOnProject: false`) — replaces the
solo `Agent(VideoStoryEditor)` decision step with a multi-agent "edit room" deliberation:
`VideoAnalyze` (`Source: ProjectFile`) → `EditRoom` (`View: Previous`) → `VideoCompile`
(`Decision: Step 2`, `AnalysisStepOrder: 1`) → `ReviewLoop(VideoReviewAgent)` looping back to the
`EditRoom` step. The `EditRoom` step emits the exact same `VideoEditDecisionOutput` shape a solo
`VideoStoryEditor` step would, so `Decision: Step 2` resolves identically to how it would against a
solo editor step. See [`docs/video-editing.md`](docs/video-editing.md) "The edit room".
A seventh, opt-in template — `video-derush-edit-graphics-room` (`AutoCreateOnProject: false`) —
replaces `video-derush-edit-graphics`' solo `Agent(MotionGraphicsPlanner)` step with a multi-agent
"graphics room": `VideoAnalyze` (`Source: ProjectFile`, `emitOverlayPlacements: true`) →
`Agent(VideoStoryEditor)` → `GraphicsRoom` (`View: Step 1` — explicit, since `Previous` would
resolve to the story editor's decision, not the placements envelope) → `VideoCompile`
(`Decision: Step 2`, `enableGraphics: true`, `graphicsPlan: Step 3`, `AnalysisStepOrder: 1`) →
`ReviewLoop(VideoReviewAgent)` looping back to step 2. The `GraphicsRoom` step emits the exact
same `MotionGraphicsPlanOutput` shape a solo `MotionGraphicsPlanner` step would, so the compile
step's `GraphicsPlan` resolution is unchanged. See [`docs/video-editing.md`](docs/video-editing.md)
"The graphics room".

### Video Editing (ffmpeg-based, `VideoAnalyze`/`VideoCompile`/`EditRoom`/`GraphicsRoom` step types)

Real source-video derushing and cutting — silence/shot detection, optional ASR transcription, an
LLM editorial decision anchored to opaque ids only (never a timestamp), and frame-accurate ffmpeg
compilation. Full design in [`docs/video-editing.md`](docs/video-editing.md); summary here:

- **`StepType.VideoAnalyze`** (`Shared/Workflows/VideoAnalyzeStepConfig.cs`) — deterministic,
  non-LLM. Probes the source (`VideoSourceRef`: a `ProjectFile`, a specific step's output, or the
  previous step's output — render outputs are **not** `ProjectFile` rows, only reachable via
  `WorkflowStepResult.OutputStorageKey`, so the source model resolves step-result keys as a
  first-class kind), detects silence/shots via ffmpeg, optionally transcribes via
  `ITranscriptionClient` (`VideoTranscriptionMode`: `Off`/`Optional`/`Required`), and emits a
  `{view, meta}` envelope (same shape as `Extract`) plus a full analysis artifact
  (`WorkflowStepResult.ArtifactStorageKey`). **Phase 1** (`AnalyzeVisuals`/`AnalyzeAudioLevels`,
  both default `true`) adds deterministic per-shot visual/audio descriptors — motion, camera-move
  classification, exposure/color, overlay-safe-zone regions, near-duplicate/best-take grouping,
  audio loudness — derived from one low-res raw-frame grid ffmpeg pass (`IFrameGridSampler`) plus a
  pure C# analyzer (`FrameGridAnalyzer`, `WavRmsSampler`); zero LLM calls, purely additive
  (`VideoAnalysisArtifact.Version` 1→2, old artifacts still deserialize). `VisualDetail`
  (`None`/`Compact`/`Full`) degrades per-shot richness *before* the bounded view ever drops an
  offered item. See `docs/video-editing.md` § "Scene/visual analysis (Phase 1)". **Phase 2**
  (`Vision`, default `Off` — deliberately *not* `Optional` like `Transcription`, since captioning
  can cost up to `MaxCaptionedShots` vision chat calls per step, not one) adds optional vision-LLM
  shot captioning: `KeyframeSelector` picks which shots to caption (`PerDuplicateGroup`/
  `LongestShots`/`EvenlySpaced`), `IKeyframeExtractor` (new `FfmpegArgvBuilder.BuildKeyframeArgs`)
  extracts one representative frame per selected shot, and `IShotCaptioner`/`VisionShotCaptioner`
  sends it through the reused `IChatClientFactory` with `ChatResponseFormat.ForJsonSchema<VideoShotCaption>()`
  for a structured scene description, surfaced as a `"c"` key in the bounded view (same
  degrade-before-drop discipline as `"v"`/`"a"`). The shot-id↔caption binding is never
  model-controlled — the executor always overwrites the model-returned `ShotId` with the id it
  actually requested. `VideoAnalysisArtifact.Version` stays at 2 (purely additive optional fields).
  See `docs/video-editing.md` § "Vision captioning (Phase 2)". **Phase 4** adds seven semantic
  visual dimensions (D1-D7: colour temperature/tone/saturation, look grouping — `view.lookGroups`,
  id namespace `k{n}`, never offered to any agent — audio character, letterbox/pillarbox, backlit
  candidate) plus an opt-in native-resolution sharpness metric; D1-D4/D6 are free (derived from the
  grid Phase 1 already samples) and default on, only sharpness costs a new ffmpeg call per shot and
  stays opt-in. Also primes the Phase 2 vision prompt with Phase 1's own measured words
  (`ShotCaptionRequest.MeasuredContext`), adds optional multi-frame contact-sheet keyframes
  (`KeyframesPerShot`, clamped 1..3), and wires `PersistKeyframes`. `VideoAnalysisArtifact.Version`
  stays at 2 (purely additive). See `docs/video-editing.md` § "Semantic visual dimensions (Phase 4)".
  **Phase 5** (`DetectInsertRegions`, default `false`) adds deterministic chroma-plate quad
  tracking for tracked screen inserts: one extra medium-res grid pass per source
  (`ChromaQuadTracker` — pure C#, no LLM, no new dependency) tracks a uniform-color plate (e.g. a
  green screen inside a phone held in frame) as a deforming quadrilateral, offered to the
  motion-graphics agent as opaque `r{n}` ids (`view.insertRegions`, `OfferedInsertRegionIds` — its
  own id namespace, never resolvable by `BuildIdTimeIndex`); the full per-frame corner data stays
  compile-time-only in the artifact. See `docs/video-editing.md` § "Tracked screen inserts (Phase 5)".
- **`StepType.Agent` + `AgentType.VideoStoryEditor`** — an LLM decides which offered ids to KEEP
  (`VideoEditDecisionOutput`); no existing step type is duplicated for this, `AgentStepExecutor`
  already provides structured output, retry-with-feedback, and tool scoping.
- **`StepType.VideoCompile`** (`Shared/Workflows/VideoCompileStepConfig.cs`) — deterministic,
  non-LLM. Resolves the agent's chosen ids to frame-accurate `[start, end)` times against the full
  analysis artifact (never trusting a model-supplied number, because there is none), then encodes
  with ffmpeg. **Phase 3** (`EnableGraphics`, default `false`) adds optional motion-graphics
  overlays applied during the same encode: deterministic overlay-placement candidates
  (`view.placements`, id namespace `p{n}` — separate from cut-anchor ids and never resolvable by
  `BuildIdTimeIndex`) are derived per-shot by `OverlayPlacementBuilder` in `VideoAnalyze`
  (`EmitOverlayPlacements`), then `AgentType.MotionGraphicsPlanner` (`MotionGraphicsPlanOutput`)
  plans zero or more overlays anchored only to those ids, and `VideoCompileStepExecutor` maps each
  chosen placement's source-timeline window through the cut to the output timeline
  (`MapSourceToOutputSec`/`MapSourceWindowToOutput`) before rendering a `drawbox`+`drawtext` per
  overlay. Overlay text is sanitized (`OverlayTextSanitizer`, an allowlist) and written to its own
  scratch file referenced via drawtext's `textfile=` (with `expansion=none`) — never interpolated
  into the ffmpeg filter string. A bad/missing graphics plan, an unknown placement id, or a missing
  `drawtext` filter all degrade to "no graphics applied" rather than failing the compile. See
  `docs/video-editing.md` § "Motion graphics (Phase 3)". `TransitionPolicy` (`VideoTransitionPolicy`,
  default `Off`) picks a per-seam transition treatment (hard cut, audio declick, dissolve, dip-to-black,
  dip-cut) purely from measured shot/seam data — no agent chooses, requests, or can alter one — plus an
  independent program-level video/audio fade-in/out at the very start/end of the compiled file
  (`ProgramFadeInMs`/`ProgramFadeOutMs`/`ProgramAudioFadeInMs`/`ProgramAudioFadeOutMs`), capped by
  `MaxTransitionSegments` since a crossfade-style blend costs more filtergraph buffering per
  transition than a plain segment switch. See `docs/video-editing.md` § "Seam transitions and the
  program envelope". **Phase 5** (`EnableInserts`, default `false`) adds tracked screen inserts:
  a Remotion scene the `MotionGraphicsPlanner` agent rendered itself (`ScreenInsert` on
  `MotionGraphicsPlanOutput` — exactly three string properties, `RegionId`/
  `RenderedAssetStorageKey`/`Reason`, pinned by `MotionGraphicsPlanOutputInvariantTests`) is
  corner-pinned INTO a tracked chroma-plate region and warps with it frame by frame, via ffmpeg's
  per-frame-animated `perspective` filter plus an identically-warped white-plate `alphamerge` mask
  (`ScreenInsertFilterBuilder`); every number in the warp comes from the artifact's deterministic
  tracking data mapped through `OutputTimeline`, never from the model. Requires `Mode = Reencode`
  (`INSERTS_REQUIRE_REENCODE`); every other failure (unknown/unoffered region id, low tracking
  confidence, bad asset key, cut-away window, missing filters, multi-source/transition-overlap
  compile) degrades to "no insert applied". See `docs/video-editing.md` § "Tracked screen inserts
  (Phase 5)".
- **`StepType.EditRoom`** (`Shared/Workflows/EditRoomStepConfig.cs`,
  `WorkflowEngine/Execution/StepExecutors/EditRoomStepExecutor.cs`) — replaces the solo
  `StepType.Agent` + `AgentType.VideoStoryEditor` decision step with a multi-agent "edit room":
  several `VideoStoryEditor`-role seats plus `AgentType.VideoEditDirector` converse in a live
  `Microsoft.Agents.AI.Workflows` group chat over the same bounded `VideoAnalyze` view, deterministically
  scheduled (`EditRoomGroupChatManager`, zero model calls of its own — round-robins the seats, then
  the director, terminating on the director's `ROOM_DECIDED` sentinel and/or offered-id-mention
  convergence). The director then emits ONE schema-validated `VideoEditDecisionOutput` — the exact
  schema/rushcut-invariant `VideoStoryEditor` already produces — in a normal structured call OUTSIDE
  the chat loop, so `VideoCompileStepExecutor` needs zero changes to consume it. Falls back to one
  ordinary solo `VideoStoryEditor` call (`EditRoomStepConfig.FallbackToSoloEditor`, default `true`)
  on any room failure. The room's transcript is optionally persisted as this step's
  `ArtifactStorageKey` for audit — free-form model prose, NEVER authoritative. Each completed turn
  also publishes an append-only `WorkflowStepChatTurn` integration event (a structural twin of
  `WorkflowStepReasoningCaptured`, not the ephemeral/supersedable `WorkflowStepProgress`). See
  `docs/video-editing.md` § "The edit room". Since the graphics room landed, all room mechanics
  (scheduling/termination, per-turn option injection, transcript persistence, synthesis retries,
  solo fallback, failure envelopes) live in the shared, room-generic `RoomGroupChatManager`/
  `RoomSeatAgent`/`RoomStepExecutorBase<TDecision>`/`IRoomStepConfig` infrastructure — a new room
  is defined by binding its id vocabulary, prompts, agent types, and validation hooks. See
  `docs/video-editing.md` § "The shared room infrastructure".
- **`StepType.GraphicsRoom`** (`Shared/Workflows/GraphicsRoomStepConfig.cs`,
  `WorkflowEngine/Execution/StepExecutors/GraphicsRoomStepExecutor.cs`) — the motion-graphics
  analogue of `EditRoom`, built on that same shared room infrastructure: several
  `MotionGraphicsPlanner`-role artist seats (`LayoutArtist`/`TimingArtist`/`CopyArtist` by
  default) plus `AgentType.MotionGraphicsDirector` deliberate over the same offered
  `view.placements` candidates (`p{n}` ids, enriched with the solo planner's `inEdit` annotation)
  a solo planner consumes, then the director synthesizes ONE `MotionGraphicsPlanOutput` — the
  exact schema/invariant the solo planner already produces, so `VideoCompileStepExecutor`'s
  `GraphicsPlan` resolution needs zero changes. Distinctive vs. the edit room: an empty overlay
  plan is a VALID outcome (a zero-placement view completes with an empty plan instead of failing,
  and zero overlays never degrades), and room-participant turns are tool-restricted to read-only
  even though the backing agent types carry sandbox+render grants (only the standalone synthesis
  call may render an overlay asset). Falls back to one ordinary solo `MotionGraphicsPlanner` call
  (`FallbackToSoloPlanner`, default `true`). See `docs/video-editing.md` § "The graphics room".

**Why ffmpeg runs inside the WorkflowEngine container, not the Remotion sandbox
(`/sandbox`):** the sandbox's container and network isolation exists to contain
*untrusted, model-authored* code (arbitrary TSX, arbitrary npm packages) — note that its
command allowlist is input hygiene, not containment, since `npm run build` executes a
caller-written `package.json` (see `docs/sandbox-service.md`). Video editing's ffmpeg
argv is built entirely by first-party C# from a validated, typed cut list — the model contributes
only opaque ids it was actually offered, never a path, flag, or timestamp — so there is nothing
untrusted for the sandbox's containment to protect against, while admitting `ffmpeg` to the
sandbox's allowlist would open one of the richest argv-injection surfaces in common Unix tooling
(`-i http://…` SSRF, `concat:`/`subfile:` arbitrary file read,
`-f lavfi` with `movie=`) for zero containment benefit. The sandbox's mechanics are also simply
wrong for large binary media: containers are `--read-only` with a 256 MB tmpfs `/tmp`, file I/O is
base64-over-JSON (doubling memory for a large file in both directions), and sandbox containers
cannot reach MinIO. `/sandbox` itself is untouched by this feature — see `docs/video-editing.md`
for the full rationale and the phase-2 escape hatch (a dedicated `video-worker` microservice) this
decision leaves open.

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

**Config:** `nginx/nginx.conf` (+ `nginx/locations.conf`, `nginx/http-server-dev.conf`/`http-server-redirect.conf`, `nginx/https-server.conf.template`, assembled at container start by `nginx/docker-entrypoint.sh`) + `nginx/auth.js` (njs module)
**Single entry:** Port 80 (`APP_PORT`) and 443 (`HTTPS_PORT`) — nginx terminates TLS itself; see [`docs/tls.md`](docs/tls.md) for the self-signed-by-default / Caddy-issued-once-you-have-a-domain design and the `caddy` sidecar that obtains and auto-renews the certificate.

| Path | Upstream | Auth |
|------|----------|------|
| `/api/v1/auth/token` | `go-api:8080` | Login response intercepted for cookie setting |
| `/api/v1/auth/*` | `go-api:8080` | Cookie → Authorization header |
| `/api/v1/admin/*` | `go-api:8080` | Cookie → Authorization header |
| `/api/v1/workflow-engine/*` | `workflow-engine:8080` | Cookie → Authorization header |
| *(not routed)* `/api/v1/sandboxes/*` | — | **Deliberately not proxied.** The sandbox executor API is in-cluster only, reached by the workflow engine with a bearer token (`SANDBOX_API_TOKEN`). It was previously proxied here with no auth, which exposed unauthenticated RCE against the container holding the Docker socket — do not re-add it |
| `/api/v1/*` | `inference:8080` | Cookie → Authorization header |
| `/health` | `go-api:8080` | None |
| `/api/auth/logout` | — | Nginx clears cookies |
| `/app/*` | `web:3000` | None (dashboard, basePath `/app`) |
| `/*` | `site:3000` | None (public marketing site) |

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
| `nginx` | Built from `./nginx` (`nginx:alpine` + openssl/gettext) | 80 (`APP_PORT`), 443 (`HTTPS_PORT`) | 80, 443 | Single entry point, njs cookie↔header translation, TLS termination (self-signed by default; see [`docs/tls.md`](docs/tls.md)) |
| `caddy` | `caddy:2-alpine` | — (internal) | 80, 443 | ACME client only — obtains/renews the Let's Encrypt cert nginx reads off the shared `caddy_certs` volume; never serves traffic. Opt-in via the `tls` compose profile, requires `DOMAIN`/`ACME_EMAIL`. See [`docs/tls.md`](docs/tls.md) |
| `site` | Built from `./site` | 127.0.0.1:3100 (dev convenience only) | 3000 | Public marketing site (Next.js, Tailwind v4), no `depends_on` — must come up even if the backend is down |
| `web` | Built from `./web` | — (internal) | 3000 | Next.js dashboard, basePath `/app` |
| `go-api` | Built from `./api` | — (internal) | 8080 | Depends on postgres (healthy) |
| `inference` | Built from `./inference` | — (internal) | 8080 | Inference API, depends on go-api + rabbitmq; mounts `dpkeys` at `/keys` (Data Protection key ring) |
| `workflow-engine` | Built from `./inference` | — (internal) | 8080 | Workflow engine, depends on inference + rabbitmq; mounts `dpkeys` at `/keys` (Data Protection key ring) and `videoscratch` at `/var/tmp/reelforge-video` (per-execution ffmpeg scratch space, `VideoEditing:ScratchPath`); image includes `ffmpeg`/`ffprobe` (see Video Editing above) |
| `sandbox-runtime` | Built from `./sandbox` (target `sandbox-runtime`) | — | — | One-shot: builds the **minimal untrusted image** each sandbox container runs, then exits. `network_mode: none` |
| `sandbox-executor` | Built from `./sandbox` (target `control-plane`) | — (internal) | 8080 | Sandbox control plane. Mounts the Docker socket, so compromise = host root. On the `reelforge` network **only** — never on the sandbox network. Requires `SANDBOX_API_TOKEN`; not proxied by nginx |
| `postgres` | `postgres:16-alpine` | 5432 (**loopback only**) | 5432 | Volume `pgdata`, healthcheck via `pg_isready` |
| `minio` | `minio/minio:latest` | 9000/9001 (**loopback only**) | 9000/9001 | Volume `miniodata`, console on 9001 |
| `minio-init` | `minio/mc:latest` | — | — | One-shot: creates the `reelforge` bucket, then exits |
| `rabbitmq` | `rabbitmq:3-management-alpine` | 5672/15672 (**loopback only**) | 5672/15672 | Volume `rabbitmqdata`, management UI on 15672; `./rabbitmq/conf.d` mounted read-only at `/etc/rabbitmq/conf.d` (`loopback_users = none` so go-api/inference/workflow-engine can authenticate as `guest` over the compose network; `consumer_timeout = 7200000` so a long `VideoAnalyze`/`VideoCompile` step is never force-killed by RabbitMQ's own 30-minute default) |
| `whisper` | `ghcr.io/speaches-ai/speaches:latest-cpu` | 127.0.0.1:`WHISPER_PORT` (default 9002) | 8000 | Local, OpenAI-Whisper-API-compatible ASR server (CPU inference, int8 compute) for `VideoAnalyze` transcription; volume `whisper-hf-cache` for downloaded model weights. Not auto-wired — register it as an `inference_providers` row (`Capability: Transcription`, `Kind: OpenAICompatible`, endpoint `http://whisper:8000`) via `/admin/inference-providers` to use it |

```bash
docker compose up --build -d              # Start full stack
docker compose up --build -d <service>    # Rebuild single service
docker compose logs -f [service-name]     # View logs
docker compose down                       # Stop everything
docker compose down -v                    # Full reset
docker compose --profile tls up -d caddy  # Obtain/renew a real cert once DOMAIN is set (docs/tls.md)
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
| `AZURE_OPENAI_ENDPOINT` | — | Azure OpenAI endpoint URL. **Fallback only** — used for chat completions when no `inference_providers` row exists or none is marked default for the `Chat` capability; configure providers at runtime via `/admin/inference-providers` instead. Embeddings for vector search always use this. There is no equivalent fallback for transcription — see `VideoEditing` below. |
| `AZURE_OPENAI_API_KEY` | — | Azure OpenAI API key (see fallback note above) |
| `AZURE_OPENAI_DEPLOYMENT` | `gpt-4o-mini` | Azure OpenAI deployment/model name (see fallback note above) |
| `APP_PORT` | `80` | Nginx reverse proxy host port (HTTP) |
| `HTTPS_PORT` | `443` | Nginx reverse proxy host port (HTTPS) |
| `DOMAIN` | — | Real domain for a Let's Encrypt certificate. Leave empty for local development (nginx falls back to a self-signed cert). See [`docs/tls.md`](docs/tls.md) |
| `ACME_EMAIL` | — | Contact email for Let's Encrypt, required by the `caddy` service once `DOMAIN` is set |
| `FORCE_HTTPS` | `false` | Redirect `http://` to `https://` in nginx. Only enable after confirming `https://<DOMAIN>` works — see [`docs/tls.md`](docs/tls.md) |
| `COOKIE_SECURE` | `false` | Sets `Secure` on the `reelforge_token`/`reelforge_user` cookies. Only enable once real TLS is serving `https://` — browsers silently drop `Secure` cookies over plain HTTP, breaking login |
| `ASPNETCORE_ENVIRONMENT` | `Development` | ASP.NET environment (`Development` / `Production`) |
| `RABBITMQ_USER` | `guest` | RabbitMQ username |
| `RABBITMQ_PASSWORD` | `guest` | RabbitMQ password |
| `RABBITMQ_PORT` | `5672` | RabbitMQ AMQP port |
| `RABBITMQ_MGMT_PORT` | `15672` | RabbitMQ management UI port |
| `WHISPER_PORT` | `9002` | Host port for the local `whisper` ASR service (`ghcr.io/speaches-ai/speaches`), bound to loopback only. Not consumed by any service's config directly — register the container as a `Transcription`-capability `inference_providers` row to actually use it |
| `WORKFLOW_MAX_CONCURRENCY` | `4` | Max parallel workflow executions |
| `SANDBOX_API_TOKEN` | — | **Required.** Shared secret for the sandbox executor API (`Sandbox__ApiToken` on the WorkflowEngine). The sandbox service exits at startup if unset — it can start containers and holds the Docker socket, so it never runs unauthenticated |
| `SANDBOX_NETWORK_EGRESS` | `false` | When `false`, the sandbox docker network is created `--internal`: sandboxed code has no route to the internet **or to the host gateway** (and therefore none of the host-published ports). Setting `true` re-enables runtime `npm install` via `POST /packages` and simultaneously gives untrusted code a network — see [`docs/sandbox-service.md`](docs/sandbox-service.md) |
| `SMTP_HOST` | — | SMTP server hostname (optional) |
| `SMTP_PORT` | `587` | SMTP server port |
| `SMTP_USERNAME` | — | SMTP auth username |
| `SMTP_PASSWORD` | — | SMTP auth password |
| `SMTP_FROM` | — | Sender email address |
| `ADMIN_EMAIL` | `admin@reelforge.local` | Initial admin user email |
| `ADMIN_PASSWORD` | — | Initial admin password (auto-generated if empty) |
| `VIDEO_MAX_CONCURRENT_JOBS` | `1` | Max ffmpeg/ffprobe invocations running concurrently in the WorkflowEngine container (`VideoEditing:MaxConcurrentJobs`) — kept low by default since encoding is CPU-heavy and competes with `WORKFLOW_MAX_CONCURRENCY` |
| `VIDEO_ANALYZE_TIMEOUT_SECONDS` | `900` | Hard wall-clock timeout for a `VideoAnalyze` step's ffmpeg/ffprobe/ASR calls (`VideoEditing:AnalyzeTimeoutSeconds`) |
| `VIDEO_COMPILE_TIMEOUT_SECONDS` | `1800` | Hard wall-clock timeout for a `VideoCompile` step's ffmpeg encode (`VideoEditing:CompileTimeoutSeconds`) |
| `VIDEO_FONT_FILE` | `/usr/share/fonts/dejavu/DejaVuSans.ttf` | Font file passed to drawtext's `fontfile=` for Phase 3 motion-graphics overlays (`VideoEditing:FontFilePath`) — must exist in the `workflow-engine` image; the default matches the `font-dejavu` Alpine package the Dockerfile installs alongside ffmpeg |
| `SITE_BUILD_TARGET` | `development` | Dockerfile build target for the `site` service (`development` for Turbopack hot reload, `production` for the precompiled standalone build) |
| `SITE_NODE_ENV` | `development` | `NODE_ENV` for the `site` container; set to `production` alongside `SITE_BUILD_TARGET=production` |
| `NEXT_PUBLIC_SITE_URL` | `https://reelforge.com` | Canonical absolute origin for the marketing site — the single value `site/lib/site-config.ts` reads for canonical URLs, `sitemap.xml`, `robots.txt`, Open Graph/Twitter cards, and JSON-LD. Baked in at build time via a Docker build arg, so the `site` image must be rebuilt after changing it. See [`docs/marketing-site.md`](docs/marketing-site.md) |
| `NEXT_PUBLIC_GA_MEASUREMENT_ID` | — | GA4 measurement ID (`G-XXXXXXXXXX`) for the marketing site. Leave empty to ship without analytics — no script is injected either way unless a visitor also accepts the cookie banner |
| `CONTACT_WEBHOOK_URL` | — | Where the marketing site's `/api/contact` route POSTs submissions as JSON. Leave empty and submissions are only logged to the `site` container's stdout |

### Inference `appsettings.json` Keys

Both services share these keys (overridden by Docker Compose env vars):

| Key | Description |
|-----|-------------|
| `ConnectionStrings:DefaultConnection` | PostgreSQL connection string |
| `Jwt:Issuer` / `Jwt:Audience` / `Jwt:SigningKey` | JWT validation (HS256) |
| `RabbitMQ:Host` / `RabbitMQ:Username` / `RabbitMQ:Password` | RabbitMQ connection |
| `AzureOpenAI:Endpoint` / `AzureOpenAI:ApiKey` / `AzureOpenAI:DeploymentName` | Fallback chat-completion backend, used only when no `inference_providers` row exists or none is default (also the only backend for embeddings) |
| `DataProtection:KeysPath` | Directory for the Data Protection key ring used to encrypt/decrypt inference provider API keys; defaults to `/keys` in code if unset. Both services must share the same path (the `dpkeys` volume) or the engine cannot decrypt keys the API wrote |
| `Inference:ProviderCacheSeconds` | TTL (seconds) for the in-memory cache of `inference_providers` rows read by `IInferenceProviderResolver`; defaults to `60` in code if unset — provider changes take effect within roughly this long, with no redeploy or event needed |
| `MinIO:Endpoint` / `MinIO:AccessKey` / `MinIO:SecretKey` / `MinIO:BucketName` | S3-compatible storage (API only) |
| `WorkflowEngine:MaxConcurrency` | Max parallel executions (Engine only) |
| `Agents:<AgentName>:SystemPrompt` | Override any agent's system prompt |
| `VideoEditing:ScratchPath` | Root directory for per-execution/per-step video scratch files (extracted audio, intermediate segments); defaults to `/var/tmp/reelforge-video` — must be a volume pre-owned by the container's non-root `$APP_UID` (Engine only) |
| `VideoEditing:FfmpegPath` / `VideoEditing:FfprobePath` | Executable name or path for ffmpeg/ffprobe; default `ffmpeg`/`ffprobe`, resolved via `PATH` (Engine only) |
| `VideoEditing:MaxConcurrentJobs` | Max ffmpeg/ffprobe invocations running concurrently across the whole process, enforced by a single process-wide semaphore; default `1` (Engine only) |
| `VideoEditing:AnalyzeTimeoutSeconds` / `VideoEditing:CompileTimeoutSeconds` | Hard wall-clock timeouts for `VideoAnalyze`/`VideoCompile` step tool invocations; default `900`/`1800` (Engine only) |
| `VideoEditing:FontFilePath` | Font file for drawtext-based Phase 3 motion-graphics overlays; default `/usr/share/fonts/dejavu/DejaVuSans.ttf` (Engine only) |
