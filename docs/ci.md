# Continuous Integration

CI only. There is no CD — ReelForge runs locally via `docker compose`, so nothing
is published or deployed from GitHub. No workflow pushes an image, creates a
release, or holds a registry/cloud credential; the only secret any of them touch
is the `GITHUB_TOKEN` CodeQL needs to upload its results.

Two workflows:

| Workflow | File | Trigger |
|----------|------|---------|
| CI | [`.github/workflows/ci.yml`](../.github/workflows/ci.yml) | push to `master`, every PR, manual |
| CodeQL | [`.github/workflows/codeql.yml`](../.github/workflows/codeql.yml) | push to `master`, PRs into `master`, weekly (Mon 04:27 UTC), manual |

## What runs

| Job | Covers | Checks |
|-----|--------|--------|
| `go` (matrix: `api`, `sandbox`) | both Go modules | `go mod tidy` is a no-op, `gofmt`, `go vet`, `go build ./...`, `go test ./... -race` + coverage artifact |
| `golangci-lint` (matrix: `api`, `sandbox`) | both Go modules | golangci-lint v2.5.0, config in [`.golangci.yml`](../.golangci.yml) |
| `dotnet` | `inference/ReelForge.sln` (Shared, Inference.Api, WorkflowEngine, WorkflowEngine.Tests) | `dotnet restore`/`build -c Release` (Roslyn + .NET analyzers run here), `dotnet test` with TRX + Cobertura coverage artifacts |
| `dotnet-format` | same solution | `dotnet format --verify-no-changes --severity warn` — **advisory, `continue-on-error: true`** (see below) |
| `node` (matrix: `web`, `site`) | both Next.js apps | `npm ci`, `tsc --noEmit`, `npm run lint` (ESLint), `npm run build` |
| `docker` (matrix: 8 images) | every Dockerfile | buildx build, no push, GHA layer cache per image. Builds `web`/`site` at their `production` target and `sandbox` at both `sandbox-runtime` and `control-plane` |
| `infra-lint` | the plumbing | actionlint (+ shellcheck on `run:` blocks), ShellCheck on `*.sh`, hadolint on all seven Dockerfiles, `docker compose config` schema check |
| `ci` | — | Aggregate gate. Point branch protection at this single check rather than the matrix legs |

CodeQL analyses `go`, `csharp`, `javascript-typescript` and `actions` with the
`security-and-quality` query suite. The two compiled languages use
`build-mode: manual` — autobuild guesses wrong with two sibling Go modules and a
solution nested under `inference/`.

## Path filtering

The `changes` job (`dorny/paths-filter`) decides which stacks a **pull request**
touched, so a copy tweak under `/site` does not rebuild eight container images.
Push events to `master` ignore the filter and run everything.

## Config files

| File | Purpose |
|------|---------|
| [`.golangci.yml`](../.golangci.yml) | Shared by both Go modules — golangci-lint walks up from its working directory to find it. `max-issues-per-linter`/`max-same-issues` are `0` so CI never silently truncates findings |
| [`.hadolint.yaml`](../.hadolint.yaml) | `failure-threshold: warning`. DL3018/DL3008 (apk/apt version pinning) are off by design; reproducibility comes from the base image tag, not from distro package pins |
| [`.github/dependabot.yml`](../.github/dependabot.yml) | Weekly updates for Actions, both Go modules, NuGet, both npm apps, and every Dockerfile base image |

## Running the same checks locally

```bash
# Go — both modules
for m in api sandbox; do
  (cd $m && gofmt -l . && go vet ./... && go build ./... && go test ./... -race)
done
golangci-lint run ./...          # from inside api/ or sandbox/

# .NET
dotnet build inference/ReelForge.sln -c Release
dotnet test  inference/ReelForge.sln -c Release --no-build

# Next.js apps
for a in web site; do
  (cd $a && npm ci && npx tsc --noEmit && npm run lint && npm run build)
done

# Infra
actionlint
shellcheck --severity=warning nginx/docker-entrypoint.sh caddy/docker-entrypoint.sh
hadolint --config .hadolint.yaml **/Dockerfile
docker compose config --quiet    # needs JWT_SIGNING_KEY + SANDBOX_API_TOKEN set
```

## Known gaps

- **`dotnet-format` is advisory.** The solution has never been run through
  `dotnet format`, so gating on it would fail on day one for reasons unrelated to
  any given change. To make it blocking: run
  `dotnet format inference/ReelForge.sln`, commit the result, then flip
  `continue-on-error` to `false` on the `dotnet-format` job.
- **`api/` has no tests.** `go test ./...` passes trivially there (`[no test
  files]`); the gate exists so the first test added is enforced from then on.
  `sandbox/` has `main_test.go` and it passes.
- **No integration tests.** Nothing in CI starts Postgres, RabbitMQ, or MinIO.
  The `dotnet` job runs the WorkflowEngine unit tests (EFCore.InMemory + Moq)
  only.
