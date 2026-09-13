# Video Editing

Automatic derushing and editing of real, uploaded or rendered video files — silence and shot
detection, optional ASR transcription, an LLM editorial decision, and a frame-accurate ffmpeg cut
— implemented as two new deterministic workflow step types plus one new built-in agent. This
document is the reference for that feature; for the surrounding workflow engine (step types,
executors, agents in general) see `CLAUDE.md`.

---

## Table of Contents

- [The three-stage shape](#the-three-stage-shape)
- [The id-anchored decision contract](#the-id-anchored-decision-contract)
- [Where artifacts live](#where-artifacts-live)
- [Config reference](#config-reference)
- [Transcription (ASR)](#transcription-asr)
- [Security: why ffmpeg is not in the sandbox](#security-why-ffmpeg-is-not-in-the-sandbox)
- [Explicitly not built](#explicitly-not-built)

---

## The three-stage shape

```
┌─────────────────┐     ┌────────────────────────┐     ┌──────────────────┐
│  StepType.       │     │  StepType.Agent         │     │  StepType.        │
│  VideoAnalyze    │────▶│  AgentType.             │────▶│  VideoCompile     │
│                  │     │  VideoStoryEditor       │     │                   │
│  deterministic,  │     │  LLM, structured output │     │  deterministic,   │
│  ffmpeg + ASR    │     │  (VideoEditDecision-    │     │  ffmpeg           │
│                  │     │   Output)               │     │                   │
└─────────────────┘     └────────────────────────┘     └──────────────────┘
   emits {view, meta}       emits an ordered list of        resolves ids to
   + a full analysis        opaque "Keep" id spans          frame-accurate
   artifact                 — never a timestamp             times, encodes
```

1. **`StepType.VideoAnalyze`** (`ReelForge.Shared/Workflows/VideoAnalyzeStepConfig.cs`,
   `WorkflowEngine/Execution/StepExecutors/VideoAnalyzeStepExecutor.cs`) — deterministic, non-LLM.
   Resolves the source video, probes it, detects silence gaps and shot/scene changes with ffmpeg,
   optionally transcribes the audio, assigns every detected item a short opaque id, and emits:
   - a bounded `{view, meta}` prompt envelope (the same shape `StepType.Extract` produces, so
     downstream `Extract` steps can compose over it unchanged), persisted to `output_json`
   - the full, non-truncated analysis document (`VideoAnalysisArtifact`), persisted separately as
     a storage-key artifact

2. **`StepType.Agent` + `AgentType.VideoStoryEditor`** — an ordinary Agent step. No new step type
   was introduced for the editorial decision: `AgentStepExecutor` already provides structured
   output (`ChatResponseFormat.ForJsonSchema<T>()`), per-agent provider resolution, retry-with-
   feedback, and tool scoping, so reusing it is a straight win. The agent is given the bounded
   view from step 1 and decides which ids to **keep**.

3. **`StepType.VideoCompile`** (`ReelForge.Shared/Workflows/VideoCompileStepConfig.cs`,
   `WorkflowEngine/Execution/StepExecutors/VideoCompileStepExecutor.cs`) — deterministic, non-LLM.
   Loads the **full** analysis artifact from step 1, resolves the agent's chosen ids to exact
   `[start, end)` times, validates and normalizes the resulting cut list, frame-quantizes it, and
   encodes the edited video with ffmpeg.

Both `VideoAnalyze` and `VideoCompile` follow the same discipline `ExtractStepExecutor`
established: they **never throw**. Every failure mode — an unresolvable source, a missing
transcription provider, an unknown id, overlapping spans — is represented as a structured failure
in the step's JSON output, because `WorkflowStepResult.OutputJson` is a `jsonb` column and an
unhandled exception there takes down the whole execution's `SaveChangesAsync`.

### Picking the source video

`VideoAnalyzeStepConfig.Source` is a `VideoSourceRef`, one of three kinds:

| Kind | Resolves via | Use case |
|---|---|---|
| `PreviousStepOutput` (default) | The latest completed step result in this execution with a non-null output storage key | "Edit whatever the previous step in this workflow rendered" — e.g. chain straight off a Remotion render step |
| `StepOutput` + `StepOrder` | That specific step's `WorkflowStepResult.OutputStorageKey` | Reference an earlier step explicitly, regardless of what runs in between |
| `ProjectFile` + `ProjectFileId` | `project_files.storage_key` | Edit a raw video/audio file the user uploaded |

This three-way split exists because **Remotion render outputs are not `ProjectFile` rows** —
`ReactRemotionSandboxTools.RenderVideoAndUploadToStorage` uploads directly to
`projects/{projectId}/outputFiles/{executionId}/{name}` and records only
`WorkflowStepResult.OutputStorageKey`, never inserting into `project_files`. A source model that
could only address `ProjectFile` ids would be unable to express "edit the video this workflow just
rendered" — the primary use case.

---

## The id-anchored decision contract

**The rushcut invariant: the story-editor agent can never emit a timestamp.** This is not a prompt
instruction the model could drift away from — it is structural. `VideoEditDecisionOutput`
(`ReelForge.Shared/Data/OutputSchemas.cs`) has no numeric, `TimeSpan`, or `DateTime` property
anywhere in it:

```csharp
public class VideoEditKeepSpan
{
    public string FromId { get; set; } = "";   // e.g. "t7" or "s2" — first offered id to keep
    public string ToId { get; set; } = "";     // last offered id to keep, inclusive
    public string Reason { get; set; } = "";   // prose only
}

public class VideoEditDecisionOutput
{
    public List<VideoEditKeepSpan> Keep { get; set; } = new();  // ordered, non-overlapping
    public string EditRationale { get; set; } = "";
    public string SuggestedTitle { get; set; } = "";
}
```

There is **no "remove" list** — everything not covered by a `Keep` span is cut. A second,
"remove ids" representation would create two ways to express the same edit and a resolution order
to get wrong, for zero benefit.

This is enforced in CI, not just by convention: `VideoEditDecisionOutputInvariantTests`
(`inference/tests/ReelForge.WorkflowEngine.Tests/VideoEditDecisionOutputInvariantTests.cs`) uses
reflection to assert that neither `VideoEditDecisionOutput` nor `VideoEditKeepSpan` exposes any
`int`/`long`/`float`/`double`/`decimal`/`TimeSpan`/`DateTime`/`DateTimeOffset` property (nullable
variants included). If a future change adds e.g. a `StartSec` field "to make things easier", this
test fails the build.

### Ids, and why "offered" is a stricter check than "exists"

Every shot, silence gap, transcript segment, and word in the full analysis artifact gets a
deterministic id, assigned by index: `s{n}` shots, `g{n}` silence gaps, `t{n}` transcript
segments, `w{n}` words (words are persisted for audit/phase-2 subtitle export but never offered to
the model — segments are the offered granularity in v1). The artifact also records
**`OfferedIds`**: exactly which ids were actually included in the bounded view shown to the model,
which can be a strict subset of every id in the full artifact once `VideoAnalyzeStepConfig`'s
view-budget trimming drops trailing items.

`VideoCompileStepExecutor` rejects any `FromId`/`ToId` that is not in `OfferedIds` — **not** merely
present somewhere in the full artifact. This closes a subtle hole: without it, a model could
reference an id it was never actually shown (e.g. one dropped by trimming), which would resolve to
a real time in the artifact but not one the model ever saw or reasoned about.

### How compile resolves ids to frame-accurate times

`VideoCompileStepExecutor`, in order — never trusting a model-emitted number, because there is
none in the schema to trust:

1. Load the full analysis artifact via the referenced `VideoAnalyze` step's `ArtifactStorageKey`
   (`AnalysisStepOrder`, or `AnalysisStepResultId` to resolve a *prior execution's* artifact —
   scoped to the same project only, see below).
2. Reject any id not in `OfferedIds` (`UNKNOWN_ID`).
3. Map each id to `[StartSec, EndSec)` from the full artifact.
4. Normalize: sort by start; require strictly increasing, non-overlapping spans (reordering kept
   spans is out of scope for v1 — a violation fails the step with a precise diagnostic that
   becomes retry feedback); coalesce adjacent spans; apply `PrePaddingMs`/`PostPaddingMs`; clamp to
   `[0, durationSec]`; drop spans shorter than `MinSegmentMs`; enforce `MaxSegments`.
5. **Frame-quantize on the exact rational fps** read from ffprobe's `r_frame_rate` (e.g.
   `30000/1001`), using integer arithmetic throughout — never a rounded `double` — so accuracy
   never depends on the source's frame rate being a whole number.
6. Evaluate `Expect` (including `MinRetainedRatio`, which refuses an edit that discards nearly
   everything the source contained).
7. Write the EDL artifact (the resolved cut list, for audit), then encode.

`AnalysisStepResultId` lets a `VideoCompile` step reference a `VideoAnalyze` result from a
**different** execution — the first-class mechanism behind the approval-by-composition workflow
below. It is resolved only against step results whose `WorkflowExecution.ProjectId` matches the
current execution's project; a cross-project reference is refused.

### Approval: composition, not a suspend/resume gate

There is no blocking mid-execution approval gate in v1 — `WorkflowExecutorService.ExecuteAsync`
has no `AwaitingApproval` status or persisted suspend point to resume from; building one is a
whole initiative of its own. Instead:

1. Run a workflow containing only `VideoAnalyze` + the `VideoStoryEditor` agent step. Inspect the
   proposed edit in the execution UI (the "Edit decision list" panel, or the `VideoAnalyze` step's
   own bounded view/artifact).
2. Run a **second** workflow or execution containing only `VideoCompile`, pointed at the first
   run's analysis artifact via `AnalysisStepResultId`.

The compiled video needs no new playback UI: writing `OutputStorageKey` under the `outputFiles`
prefix makes it appear in `GET /outputs` and in the existing execution-page `<video>` player
automatically, exactly like a Remotion render.

---

## Where artifacts live

| Artifact | Storage key | Referenced by |
|---|---|---|
| Full analysis JSON (shots + silences + words + segments + `OfferedIds` + provenance — can be several MB) | `projects/{projectId}/agentFiles/video-analysis/{executionId}/step-{order}-analysis.json` | `WorkflowStepResult.ArtifactStorageKey` on the `VideoAnalyze` result |
| Bounded prompt view `{view, meta}` (≤ `MaxOutputChars`, default 24,000) | `WorkflowStepResult.OutputJson` (jsonb) | Agent input / step history, same as any other step |
| Resolved EDL (the compile step's own cut list, for audit) | `projects/{projectId}/agentFiles/video-analysis/{executionId}/step-{order}-edl.json` | `WorkflowStepResult.ArtifactStorageKey` on the `VideoCompile` result |
| Edited mp4 | `projects/{projectId}/outputFiles/{executionId}/{OutputFileName}` | `WorkflowStepResult.OutputStorageKey` **and** a new `project_files` row (category `outputFiles`, mime `video/mp4`) |
| Working files (extracted wav, intermediate segment files) | `{VideoEditing:ScratchPath}/{executionId}/{stepId}/` (default `/var/tmp/reelforge-video`) | Nothing — deleted in a `finally` block once the step completes |

**`ArtifactStorageKey` is a separate column from `OutputStorageKey` on purpose.**
`OutputsController.ListOutputs` treats any step result whose `OutputStorageKey` starts with
`projects/{id}/outputFiles` as a playable video, and the execution UI renders a `<video src=…>`
for it. Putting the analysis JSON or EDL there would make the UI try to play JSON as a video.
Keeping them in a separate column under a separate `agentFiles/video-analysis` prefix avoids that
entirely, and lets a dedicated endpoint validate against a narrower, non-playable prefix:

```
GET /api/v1/projects/{projectId}/step-results/{stepResultId}/artifact
```

Mirrors `OutputsController.DownloadOutput`'s guards exactly (project exists → caller owns it →
step result resolved through `WorkflowExecution.ProjectId`) plus one more: the storage key must
start with `projects/{projectId}/agentFiles/video-analysis` — never `outputFiles` — so this
endpoint can never be coerced into serving a playable render or another project's object.

**Registering the edited video as a `ProjectFile`** (unlike Remotion render outputs, which are
not) is deliberate: it makes an edited video re-editable via `VideoSourceKind.ProjectFile` and
visible in the project's file list. It's created with `SummaryStatus = Done` and
`IndexingStatus = NotIndexed` so the text summarizer/vector chunker never touches a binary — see
the R22 fix below.

### Uploaded video/audio never reaches the text pipeline (R22)

`ProjectFilesController.Upload` (and the folder-move and bulk-reindex endpoints) skip both
summarization-queue enqueue and vector-indexing enqueue when the file's MIME type starts with
`video/` or `audio/`, setting `SummaryStatus = Done` and `IndexingStatus = NotIndexed` immediately
instead of the normal `Pending` → summarizer/chunker pipeline. Without this, a large uploaded mp4
or wav would be handed to `ProjectFileIndexingConsumer`, which reads the object as UTF-8 text
before chunking it — wasted compute at best, a very large in-memory string at worst.

---

## Config reference

### `VideoAnalyzeStepConfig`

| Field | Default | Notes |
|---|---|---|
| `Version` | — | Config schema version |
| `Source` | — | `VideoSourceRef` — see [Picking the source video](#picking-the-source-video) |
| `DetectSilence` | `true` | ffmpeg `silencedetect` |
| `SilenceThresholdDb` | `-34.0` | |
| `MinSilenceMs` | `350` | |
| `DetectShots` | `true` | ffmpeg scene-change detection |
| `SceneThreshold` | `0.30` | |
| `Transcription` | `Optional` | `Off` / `Optional` / `Required` — see [Transcription](#transcription-asr) |
| `TranscriptionProviderId` | `null` | Explicit override; otherwise resolved via the default `Transcription`-capability provider |
| `Language` | `null` | Passed through to the ASR provider |
| `WordTimestamps` | `true` | |
| `MaxAsrChunkBytes` | `20,000,000` | ASR chunk size cap (~10.4 min of 16kHz mono s16 WAV); long audio is split at silence-boundary-aligned chunks, never mid-word |
| `MaxDurationSeconds` | `1800` | Guardrail, checked **before** any decode |
| `MaxInputBytes` | `2,000,000,000` | Guardrail, checked before any decode |
| `MaxOutputChars` | `24,000` | Prompt-view budget — trimming drops whole trailing items and re-serializes, never truncates mid-JSON (same rule as `ExtractStepConfig`) |
| `MaxViewSegments` | `400` | |
| `MaxSegmentTextChars` | `160` | |
| `Expect` | `null` | Optional structural checks (`MinShots`, `MinTranscriptSegments`, `MaxSilenceRatio`) |

### `VideoCompileStepConfig`

| Field | Default | Notes |
|---|---|---|
| `Version` | — | Config schema version |
| `Decision` | — | `ExtractInputRef` (reused verbatim from `StepType.Extract`) — only `From = Previous` or `From = Step` are valid here |
| `AnalysisStepOrder` | — | Which `VideoAnalyze` step's artifact to resolve ids against |
| `AnalysisStepResultId` | `null` | Cross-execution override — resolve a prior run's artifact instead of this execution's; scoped to the same project |
| `Mode` | `Reencode` | `Reencode` (frame-accurate `select`/`aselect` filtergraph) or `StreamCopy` (fast, lossless, but cuts snap to keyframes) |
| `PrePaddingMs` / `PostPaddingMs` | `80` / `120` | |
| `MinSegmentMs` | `250` | Spans shorter than this are dropped |
| `MaxSegments` | `200` | Above ~64 segments the filtergraph is written to a scratch file and passed via `-filter_complex_script` to avoid argv length limits |
| `AllowKeyframeSnapping` | `false` | Must be `true` to use `Mode = StreamCopy` |
| `OutputFileName` | `"edited.mp4"` | Sanitized to a safe character set with a forced extension |
| `VideoCodec` | `"libx264"` | Allowlisted (`libx264`, `libx265`, `libvpx-vp9`) — config is workflow-author-supplied, not model output, but still reaches ffmpeg argv |
| `AudioCodec` | `"aac"` | Allowlisted (`aac`, `libmp3lame`, `copy`) |
| `Crf` | `20` | Clamped `0..51` |
| `Preset` | `"veryfast"` | Allowlisted (`ultrafast` … `veryslow`) |
| `RegisterProjectFile` | `true` | Registers the compiled video as a re-editable `ProjectFile` row |
| `Expect` | `null` | Optional structural checks (`MinOutputSeconds`, `MaxOutputSeconds`, `MinRetainedRatio` default `0.15`, `MaxRetainedRatio`) |

### The `video-derush-edit` template

An opt-in workflow template (`AutoCreateOnProject: false`, seeded in
`ReelForge.Shared/Workflows/WorkflowTemplateCatalog.cs`) demonstrating the full pipeline:
`VideoAnalyze` (`Source: PreviousStepOutput`) → `Agent(VideoStoryEditor)` → `VideoCompile`
(`Decision: Previous`, `AnalysisStepOrder: 1`). Its literal seeded JSON is deserialization-tested
against the real config types in
`WorkflowTemplateCatalogConfigDeserializationTests.cs`, so a future field-name drift between the
template and the config records it targets fails CI rather than a live workflow run.

---

## Transcription (ASR)

Ships in full — not deferred — via the same `InferenceProvider` model chat completions already
use, distinguished by a new `Capability` column (`Chat` or `Transcription`). A single provider row
can't serve both roles: a Whisper deployment is a different deployment from a chat deployment, and
many OpenAI-compatible chat gateways have no `/audio/transcriptions` endpoint at all (only
whisper.cpp-server/faster-whisper-server/speaches/LiteLLM-style deployments do). `Chat` and
`Transcription` each have their own independent "at most one default" constraint (a composite
unique index on `(capability, is_default)`), and every resolution path — chat and transcription —
filters on its own `Capability` explicitly.

`VideoAnalyzeStepConfig.Transcription` has three modes:

- **`Off`** — silence + scene + loudness only. Deterministic, zero external dependency, always
  available.
- **`Optional`** (default) — attempt ASR; if no `Transcription`-capability provider resolves, or
  the call fails after a small bounded retry, degrade cleanly to `Off` and record
  `meta.transcription.degraded = true` in the bounded view.
- **`Required`** — fail the step with a precise diagnostic if ASR is unavailable, rather than
  silently shipping an edit decision with no transcript context.

Long audio is chunked at silence-boundary-aligned points (never mid-word) by the pure,
independently-tested `TranscriptChunkPlanner`, and each chunk's word/segment timestamps are
offset by that chunk's absolute start before concatenation — the single most likely correctness
bug in this feature class, and the reason it has a dedicated multi-chunk offset test.

The admin UI for configuring providers (`/admin/inference-providers`) exposes `Capability` as a
field on create/edit; the per-agent chat-provider override picker filters `Transcription` rows
out, since that override only ever feeds chat resolution.

---

## Security: why ffmpeg is not in the sandbox

**ffmpeg and ffprobe run as first-party, non-AI-authored C# code inside the WorkflowEngine
container.** The Remotion sandbox service (`/sandbox`) — its allowlist, its images, its threat
model — is untouched by this feature.

The sandbox's command allowlist is narrow *because the code running inside it is untrusted*: it
executes model-authored TSX and installs model-chosen npm packages. Admitting `ffmpeg` to that
allowlist would open one of the richest argv-injection surfaces in common Unix tooling —
`-i http://…` (SSRF into the sandbox's bridge network, which is a normal routable Docker network,
not `--network none` — see `docs/sandbox-service.md`), the `concat:`/`subfile:`/`file:` protocols
(arbitrary in-container file read), `-f lavfi` with `movie=`, arbitrary output paths, arbitrary
`-map`. Making that safe requires an argv validator at least as strict as simply constructing the
argv yourself server-side — at which point the sandbox's containment has bought nothing, and a
hardened boundary has been widened for free.

Meanwhile, the containment property the sandbox exists to provide is **not needed here**: this
feature's ffmpeg argv is built entirely by first-party C# from a validated, typed cut list. The
model's only contribution is a set of opaque ids drawn from a set the system itself issued — not
one model-originated character reaches an ffmpeg argv.

The sandbox's mechanics are also concretely wrong for large binary media: containers run
`--read-only` with a 256 MB tmpfs `/tmp`; sandbox file I/O is base64-over-JSON (a 400 MB mp4
becomes a ~533 MB base64 string materialized in .NET memory in both directions); sandbox
containers cannot reach MinIO, so a video routed through the sandbox would round-trip through the
engine anyway; and sandbox container lifetime is keyed to `workflowExecutionId` and janitor-TTL'd
— the wrong lifecycle for a step that must produce a durable artifact.

**Costs of running ffmpeg in-process, and how they're mitigated:**

| Cost | Mitigation |
|---|---|
| CPU-heavy encoding competes with `WorkflowEngine:MaxConcurrency` | A singleton `SemaphoreSlim` in `IVideoToolRunner`; `VideoEditing:MaxConcurrentJobs` defaults to `1`. The semaphore wait is cancellable and excluded from the ffmpeg timeout. |
| ffmpeg parses untrusted, user-uploaded media (a native attack surface) | `-nostdin -hide_banner -y -protocol_whitelist file` on every invocation (`-loglevel error` for encoding; raised to `info` only for silence/shot detection, since those tools log their markers at ffmpeg's info level and the detector parses them from stderr); every input path is asserted to be under the per-execution scratch dir; pre-decode caps on byte size and probed duration; the process runs as the container's existing non-root `$APP_UID`. |
| Zombie processes on cancel/shutdown | `ct.Register(() => proc.Kill(entireProcessTree: true))` plus a hard wall-clock timeout. |
| Container image grows (ffmpeg + its dependencies) | Accepted as the cost of this design; ffmpeg is an Alpine package, not a large custom build. Measured (WS7): the built `workflow-engine` image is ~183 MB larger than the otherwise-identical `inference` image built from the same base (`mcr.microsoft.com/dotnet/aspnet:9.0-alpine`) — mostly ffmpeg's own codec dependency tree (libx264, libx265, libvpx, libaom, libsvtav1, vulkan loader, etc.), not the ffmpeg binary itself. |

**The documented phase-2 escape hatch**, if this ever needs to scale independently or run with
different trust boundaries, is a dedicated `video-worker` microservice mirroring the sandbox's
per-job container model — correct long-term shape, disproportionate for a first iteration. All
ffmpeg invocation sits behind a single `IVideoToolRunner` interface specifically so that swap is a
one-class change later.

---

## Explicitly not built

**Editing scope:** no reordering of kept spans (v1 requires strictly increasing, non-overlapping
spans); no B-roll or asset insertion; no multicam; no picture-in-picture; no speed ramps; no
transitions between cuts (hard cuts only).

**Post scope:** no colour grading / LUTs / filters / stabilization; no loudness normalization
(`loudnorm` is measured and reported only, never applied); no music bed or ducking; no subtitle
burn-in and no SRT/VTT export (the transcript exists, so this is the most obvious phase-2 add); no
speaker diarization.

**Interchange:** no EDL/AAF/FCPXML/OTIO export. The internal EDL JSON is an audit artifact, not an
interchange format.

**Timecode:** no drop-frame handling, no SMPTE timecode parsing or emission, no timecode tracks.
Remotion's own template renders integer 30 fps h264/mp4, and an uploaded video's rational fps
(e.g. `30000/1001`) is handled exactly via integer frame arithmetic without needing a timecode
subsystem.

**Delivery:** no streaming/HLS packaging, no proxy/preview transcodes, no thumbnail sprites, no
waveform PNGs.

**Approval:** no suspend/resume human-approval gate mid-execution — "approval by composition"
(run analysis + decision, inspect, then run compile separately against the saved artifact) instead.
A real suspend/resume gate is the single highest-value phase-2 item.

**Platform:** no changes to `/sandbox` whatsoever — not the Go source, not the allowlist, not the
images, not the compose entry. No new microservice in v1. No async/polling execution model beyond
what the workflow engine already has. No `ProjectFile` backfill for historical render outputs.

**Limits:** `MaxDurationSeconds` defaults to 1800 (30 min), `MaxInputBytes` to 2 GB — revisit if
real inputs are much longer or shorter. No GPU encoder path (e.g. `h264_nvenc`) unless the
deployment host is confirmed to have one.
