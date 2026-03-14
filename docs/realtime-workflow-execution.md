# Realtime Workflow Execution (SSE)

This document describes the production-ready realtime execution updates flow across Go API, WorkflowEngine events, and the execution detail UI.

## Overview

- Transport: Server-Sent Events (SSE)
- Event source of truth: WorkflowEngine events published via RabbitMQ
- Go API role: read-only consumer of RabbitMQ workflow events and SSE fanout gateway
- Frontend role: execution page subscribes to per-execution SSE stream and updates step bubbles live

## Endpoints

### Per-execution stream (primary)

- `GET /api/v1/workflows/executions/{executionId}/events`
- Auth required (`middleware.Auth`)
- Stream content type: `text/event-stream`
- Keepalive: comment ping (`: ping`) every 25s

Security rules:

1. Execution must exist
2. Access is allowed for:
   - Admin users, or
   - Execution owner (`initiated_by_user_id`), or
   - Project owner fallback when `initiated_by_user_id` is null
3. Unauthorized accesses return `403`; missing executions return `404`

Legacy compatibility route (still available):

- `GET /api/v1/projects/{projectId}/workflows/{workflowId}/executions/{executionId}/events`
- Adds strict tuple validation (`executionId`, `projectId`, `workflowId`) before streaming

### Global stream (filtered)

- `GET /api/v1/workflows/events`
- Auth required
- Admin users: can receive all execution events
- Non-admin users: receive only events for executions they own

## SSE operational guards

- Per-user stream cap enforced in Go API hub (`maxConnectionsPerUser = 3`)
- If cap exceeded, endpoint returns `429 Too Many Requests`
- Slow clients are non-blocking and may drop events (bounded channel strategy)

## Event contracts

Event contracts were extended with optional metadata fields for richer UI rendering while remaining backward-compatible.

### `WorkflowStepCompleted` optional fields

- `projectId`
- `workflowDefinitionId`
- `stepOrder`
- `stepLabel`
- `stepType`
- `iterationNumber`
- `agentType`

### `WorkflowExecutionCompleted` optional fields

- `workflowDefinitionId`
- `initiatedByUserId`

### `WorkflowExecutionFailed` optional fields

- `projectId`
- `workflowDefinitionId`
- `initiatedByUserId`

## Frontend execution page behavior

Execution detail page uses `useExecutionStream` and subscribes to the per-execution endpoint.

- Reconnect strategy: exponential backoff (1s to 8s)
- Stream state surfaced in UI: `connecting | connected | reconnecting | closed`
- Live event rail updates for:
  - `step.completed`
  - `execution.completed`
  - `execution.failed`
- Step bubbles and side insights refresh on incoming event snapshots

## Notes

- SSE payloads are treated as eventually consistent. UI performs snapshot refresh on new events to ensure final state correctness.
- Existing consumers remain compatible because new event fields are optional.
