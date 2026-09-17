# Video Editing

Automatic derushing and editing of real, uploaded or rendered video files — silence and shot
detection, optional ASR transcription, an LLM editorial decision, and a frame-accurate ffmpeg cut,
extended with multi-clip cutting, motion graphics, and background music — implemented as two new
deterministic workflow step types plus four new built-in LLM agents (`VideoStoryEditor`,
`MotionGraphicsPlanner`, `VideoReviewAgent`, `MusicSupervisor`) and one deterministic placeholder
agent (`VideoTransform`). This document is the reference for that feature; for the surrounding
workflow engine (step types, executors, agents in general) see `CLAUDE.md`.

---

## Table of Contents

- [The three-stage shape](#the-three-stage-shape)
- [The id-anchored decision contract](#the-id-anchored-decision-contract)
- [Where artifacts live](#where-artifacts-live)
- [Config reference](#config-reference)
- [Multiple source clips](#multiple-source-clips)
- [Scene/visual analysis (Phase 1)](#scenevisual-analysis-phase-1)
- [Vision captioning (Phase 2)](#vision-captioning-phase-2)
- [Transcription (ASR)](#transcription-asr)
- [Motion graphics (Phase 3)](#motion-graphics-phase-3)
- [Background music](#background-music)
- [Semantic visual dimensions (Phase 4)](#semantic-visual-dimensions-phase-4)
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
   encodes the edited video with ffmpeg. Optionally (Phase 3, `EnableGraphics`), also composites
   motion-graphics overlays during that same encode — see
   [Motion graphics (Phase 3)](#motion-graphics-phase-3).

An optional fourth stage — `StepType.Agent` + `AgentType.MotionGraphicsPlanner` — can sit between
steps 2 and 3, planning overlays from placement candidates step 1 derived alongside its usual cut
anchors. It follows the exact same shape as step 2 (an ordinary Agent step, no new step type). See
[Motion graphics (Phase 3)](#motion-graphics-phase-3) for the full design.

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
| `Source` | — | `VideoSourceRef` — see [Picking the source video](#picking-the-source-video). Ignored when `Sources` is non-empty |
| `Sources` | `null` | Multi-source addition — `IReadOnlyList<VideoSourceRef>`. When non-empty, the AUTHORITATIVE list of source clips analyzed into ONE merged artifact; `null`/empty (default) falls back to treating `[Source]` as a one-element list — see [Multiple source clips](#multiple-source-clips) |
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
| `AnalyzeVisuals` | `true` | Phase 1: one low-res grid ffmpeg pass + pure C# analyzer — see [Scene/visual analysis (Phase 1)](#scenevisual-analysis-phase-1) |
| `VisualSampleFps` | `2.0` | Grid sample rate, clamped downward by `MaxVisualSampleFrames` for long videos |
| `VisualGridWidth` / `VisualGridHeight` | `32` / `18` | Downscaled grid resolution the analyzer runs against |
| `MaxVisualSampleFrames` | `4000` | Caps the grid buffer size: `effectiveFps = min(VisualSampleFps, MaxVisualSampleFrames / durationSec)` |
| `StillMotionThreshold` | `0.02` | Per-frame motion (0..1) below which a moment counts as "still" |
| `MinStillWindowMs` | `400` | Minimum duration for a still run to be reported as a `StillWindow` |
| `MaxStillWindowsPerShot` | `3` | Longest still windows kept per shot |
| `DetectLetterbox` | `true` | D6: implemented (Phase 4), free from the grid data Phase 1 already samples — see [Semantic visual dimensions (Phase 4)](#semantic-visual-dimensions-phase-4). Default changed from `false`; may under-report soft/gradient letterbox edges |
| `DetectSharpness` | `false` | D-adjacent "sharpness": implemented (Phase 4) but costs one extra native-resolution ffmpeg invocation per measured shot (capped by `MaxSharpnessShots`), so it stays opt-in unlike the other free dimensions — see [Semantic visual dimensions (Phase 4)](#semantic-visual-dimensions-phase-4) |
| `AnalyzeAudioLevels` | `true` | Phase 1: `WavRmsSampler` over the WAV already extracted for transcription, or extracted fresh if transcription is off |
| `DetectNearDuplicates` | `true` | Phase 1: near-duplicate/best-take grouping via `FrameGridAnalyzer.GroupDuplicates` |
| `DuplicateSimilarityThreshold` | `0.90` | Minimum signature similarity (0..1) for two shots to be grouped |
| `DuplicateWindowShots` | `20` | Single-linkage grouping only compares a shot against the previous N shots (multi-take shots are temporally adjacent) |
| `VisualDetail` | `Compact` | `None` / `Compact` / `Full` — how much per-shot visual/audio detail the bounded view includes; degrades toward `None` before any item is ever dropped — see below |
| `MaxViewDuplicateGroups` | `20` | Caps `view.duplicateGroups` |
| `AnalyzeColorGrading` | `true` | Phase 4, D1-D3 (colour temperature, tone curve, saturation character) — free, three adds and one array increment inside a pixel loop that already runs — see [Semantic visual dimensions (Phase 4)](#semantic-visual-dimensions-phase-4) |
| `DetectLookGroups` | `true` | Phase 4, D4 look grouping (`view.lookGroups`, ids `k{n}`) — free, O(shots²) over six floats, cheaper than the 576-float duplicate grouping already running |
| `LookSimilarityThreshold` | `0.88` | Minimum look similarity (0..1) for two shots to share a look group; lower than `DuplicateSimilarityThreshold` since look distance is a weighted six-vector, not a 576-float grid comparison |
| `MaxViewLookGroups` | `12` | Caps `view.lookGroups` |
| `Vision` | `Off` | Phase 2: `Off` / `Optional` / `Required` — vision-LLM shot captioning, **off by default** (unlike `Transcription`) — see [Vision captioning (Phase 2)](#vision-captioning-phase-2) |
| `VisionProviderId` | `null` | Explicit override; otherwise resolved via the default `Vision`-capability provider |
| `CaptionSelection` | `PerDuplicateGroup` | `PerDuplicateGroup` / `LongestShots` / `EvenlySpaced` — which shots get captioned; round-robins across source clips when more than one is analyzed (Phase 4) |
| `MaxCaptionedShots` | `50` | Hard cap on vision chat-completion calls **across the whole step** (genuinely step-wide since the Phase 4 vision hoist — see [Vision captioning (Phase 2)](#vision-captioning-phase-2)), not a target: every shot under `MinCaptionShotSeconds` is excluded before this cap even applies. Default raised from `24` |
| `MinCaptionShotSeconds` | `1.0` | Shots shorter than this are never selected for captioning |
| `KeyframeMaxWidth` | `512` | Max width (px) of the extracted keyframe JPEG (or contact sheet — see `KeyframesPerShot`) sent to the vision model; never upscaled |
| `VisionTimeoutSeconds` | `120` | Aggregate wall-clock budget for the whole captioning pass (not per-shot) |
| `MaxCaptionChars` | `320` | Caption `summary` field is truncated to this length |
| `KeyframesPerShot` | `1` | Phase 4: frames combined into ONE contact-sheet keyframe per captioned shot, clamped `1..3`. `1` (default) is a single mid-shot still, byte-identical to the pre-Phase-4 vision path — see [Semantic visual dimensions (Phase 4)](#semantic-visual-dimensions-phase-4) |
| `PersistKeyframes` | `false` | Phase 4: now wired — when `true`, each captioned shot's keyframe JPEG is uploaded to storage under the `video-analysis/{executionId}/step-{n}-keyframes/` prefix (a persist failure never costs the caption itself). When `false` (default), keyframes stay scratch-only and are deleted with the rest of scratch space |
| `EmitOverlayPlacements` | `false` | Phase 3: derives deterministic overlay-placement candidates (`view.placements`) from each shot's Phase 1 region data — see [Motion graphics (Phase 3)](#motion-graphics-phase-3) |
| `MaxPlacementsPerShot` | `2` | Top-N regions (by `Suitability`) offered per shot |
| `MaxPlacements` | `40` | Hard cap on placements across the whole artifact; lowest-suitability candidates dropped first |
| `MaxTimeSlicesPerRegion` | `3` | When a region's chosen time window is long enough to hold more than one distinct overlay moment, split it into up to this many non-overlapping, evenly-spaced sub-windows instead of offering every overlay the same window — see [Motion graphics (Phase 3)](#motion-graphics-phase-3) |
| `MaxSharpnessShots` | `24` | Phase 4: step-wide ceiling on sharpness measurements when `DetectSharpness` is on — genuinely step-wide like `MaxCaptionedShots`, not per source. Costs one extra ffmpeg invocation per measured shot |
| `OfferMusicTracks` | `false` | Enumerates every `audio/*` project file as an `m{n}` music-track candidate (`view.musicTracks`) for a downstream `AgentType.MusicSupervisor` step — project-level, not per-source. See [Background music](#background-music) |
| `MaxMusicTracks` | `20` | Caps `view.musicTracks` |
| `Expect` | `null` | Optional structural checks (`MinShots`, `MinTranscriptSegments`, `MaxSilenceRatio`, `MinShotsWithVisuals`) |

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
| `GraphicsPlan` | `null` | Phase 3: `ExtractInputRef` (`Previous`/`Step` only) — which step's resolved `MotionGraphicsPlanOutput` to apply. `null` = no graphics looked up. See [Motion graphics (Phase 3)](#motion-graphics-phase-3) |
| `EnableGraphics` | `false` | Phase 3: applies the resolved graphics plan during the same encode. `false` (default) is byte-identical to the pre-Phase-3 compile path. Requires `Mode = Reencode` |
| `MaxOverlays` | `20` | Cap on applied overlays; excess dropped (recorded in the `graphics` block) |
| `OverlayShortMs` / `OverlayMediumMs` / `OverlayHoldMs` | `1500` / `3000` / `6000` | Milliseconds an overlay stays on screen, keyed by the model's `Duration` word (`Short`/`Medium`/`Hold`) |
| `OverlayFadeMs` | `300` | Fade-in/fade-out duration at each end of an overlay's on-screen window |
| `OverlayFontSizePct` | `5` | Percent of frame height; clamped `2..12` at execution time |
| `OverlayBoxHeightPct` | `16` | Percent of FRAME height the drawn overlay box (drawbox/drawtext background, or the box a rendered-asset overlay is stretch-scaled into) occupies — a compact accent strip, not the named safe-zone band's own height. Clamped `6..40`, never exceeding the band's own height |
| `OverlayBoxWidthPct` | `82` | Percent of the named band's own WIDTH the drawn overlay box occupies, centered. Clamped `30..100` |
| `OverlayFontColor` | `"white"` | Allowlisted (`white`/`black`/`yellow`/`#RRGGBB`) — reaches ffmpeg's filter string, so validated like `VideoCodec` |
| `OverlayBoxColor` | `"black@0.45"` | Allowlisted (`black@0.45`, `black@0.6`, `white@0.4`, `none`) |
| `MaxOverlayTextChars` / `MaxOverlaySubtextChars` | `80` / `60` | Sanitized-text truncation budget (`OverlayTextSanitizer`) |
| `MusicPlan` | `null` | Background music: `ExtractInputRef` (`Previous`/`Step` only) — which step's resolved `MusicPlanOutput` to apply. `null` = no plan looked up (deterministic `MusicTrackProjectFileId` path, or no music, is used instead). See [Background music](#background-music) |
| `MusicTrackProjectFileId` | `null` | A specific `audio/*` project file to use as the music track — the deterministic path (no agent required), and also the fallback when `MusicPlan` is unresolvable/invalid or names an unoffered track id |
| `EnableMusic` | `false` | Applies the resolved music track during the same encode. `false` (default) is byte-identical to the pre-music compile path. Requires `Mode = Reencode` and `AudioCodec != "copy"` |
| `MusicDucking` | `SpeechEnvelope` | `Off` / `SpeechEnvelope` — `SpeechEnvelope` lifts the music during non-speech windows via a deterministic keyframed `volume` envelope; `Off` is a constant ducked bed throughout |
| `MusicFitPolicy` | `LoopToFit` | `LoopToFit` (loops via `-stream_loop -1` to fill the whole edit, then trims to its exact length) / `PlayOnce` (plays once, trimmed to its own length if shorter than the edit) |
| `MusicFadeInMs` / `MusicFadeOutMs` | `1500` / `2500` | Fade duration at the start/end of the music track's own play window |
| `MusicBedQuietDb` / `MusicBedBalancedDb` / `MusicBedFeatureDb` | `-26` / `-20` / `-14` | Bed level (dBFS), keyed by the model's `Intensity` word (`Quiet`/`Balanced`/`Feature`). Clamped `[-40, -6]` |
| `MusicDuckLightDb` / `MusicDuckNormalDb` / `MusicDuckHeavyDb` | `-6` / `-11` / `-18` | Attenuation (dB, below the bed) applied while dialogue is present, keyed by the model's `Ducking` word. Clamped `[-30, 0]` |
| `MusicDuckRampMs` | `400` | Linear gain ramp (ms) INSIDE each lift window — the music is never above the ducked level exactly at a window boundary |
| `MinMusicLiftWindowMs` | `1200` | Non-speech windows shorter than this (and shorter than twice the ramp) are never lifted at all |
| `MaxMusicLiftWindows` | `12` | Caps the volume-envelope expression's length; excess windows dropped, longest first, then re-sorted chronologically |
| `MusicLiftMergeMs` | `400` | Lift windows closer together than this are merged into one |
| `Expect` | `null` | Optional structural checks (`MinOutputSeconds`, `MaxOutputSeconds`, `MinRetainedRatio` default `0.15`, `MaxRetainedRatio`) |

### The `video-derush-edit` template

An opt-in workflow template (`AutoCreateOnProject: false`, seeded in
`ReelForge.Shared/Workflows/WorkflowTemplateCatalog.cs`) demonstrating the full pipeline:
`VideoAnalyze` (`Source: PreviousStepOutput`) → `Agent(VideoStoryEditor)` → `VideoCompile`
(`Decision: Previous`, `AnalysisStepOrder: 1`). Its literal seeded JSON is deserialization-tested
against the real config types in
`WorkflowTemplateCatalogConfigDeserializationTests.cs`, so a future field-name drift between the
template and the config records it targets fails CI rather than a live workflow run.

### The `video-derush-edit-graphics` template

A fourth opt-in template (`AutoCreateOnProject: false`), extending `video-derush-edit` with Phase
3 motion graphics end to end: `VideoAnalyze` (`Source: ProjectFile`, `EmitOverlayPlacements: true`)
→ `Agent(VideoStoryEditor)` → `Agent(MotionGraphicsPlanner)` → `VideoCompile`
(`Decision: Step 2`, `AnalysisStepOrder: 1`, `EnableGraphics: true`, `GraphicsPlan: Step 3`).
`Decision`/`GraphicsPlan` reference their source steps explicitly by `StepOrder` rather than
`Previous`, since `Previous` relative to the compile step would resolve to the
`MotionGraphicsPlanner` step's output, not the story editor's decision. Deserialization-tested the
same way as `video-derush-edit`.

### The `video-derush-edit-music` template

A fifth opt-in template (`AutoCreateOnProject: false`), extending `video-derush-edit` with
background music instead of graphics: `VideoAnalyze` (`Source: ProjectFile`,
`OfferMusicTracks: true`) → `Agent(VideoStoryEditor)` → `Agent(MusicSupervisor)` → `VideoCompile`
(`Decision: Step 2`, `AnalysisStepOrder: 1`, `EnableMusic: true`, `MusicPlan: Step 3`). Same
explicit-`StepOrder` rationale as `video-derush-edit-graphics` above (`Previous` relative to the
compile step would resolve to the `MusicSupervisor` step's own output, not the story editor's
decision). See [Background music](#background-music). Deserialization-tested the same way as the
other two templates.

---

## Multiple source clips

`VideoAnalyzeStepConfig.Sources` (plural — `IReadOnlyList<VideoSourceRef>`) lets one `VideoAnalyze`
step analyze several source clips (e.g. multiple takes, camera angles, or B-roll of the same scene)
into ONE merged artifact, which a single `VideoStoryEditor` decision and a single `VideoCompile`
step can then cut across. It is a strict superset of the original single-clip behavior: `Sources`
null/empty (the default) is treated as a one-element `[Source]` list, so every existing
persisted/template config — which only ever set the singular `Source` — keeps deserializing and
behaving byte-identically. A one-element `Sources` list behaves identically to the equivalent
single-`Source` config too; there is no separate "N=1" code path anywhere in this addition.

### Analysis: independent per-clip passes, merged into one global id space

`VideoAnalyzeStepExecutor` analyzes each clip independently via the exact same deterministic
per-source pipeline it always ran (silence/shot detection, transcription, Phase 1/2/3/4 analysis),
processed sequentially, never in parallel, one clip's local file at a time. The pre-decode
guardrails (`MaxDurationSeconds`, `MaxInputBytes`) are enforced **per source clip**, not summed
across all of them.

Each clip's own ids restart at `s0`/`g0`/`t0`/`w0`/`p0`/`d0`. `VideoAnalyzeStepExecutor.OffsetId`
then remaps every local id into ONE globally-unique id space via a running per-id-kind offset
(assigned contiguously across every source file, in analysis order), and every shot/silence
gap/segment/word/placement/duplicate-group is tagged with the `SourceIndex` of the clip it came
from. The bounded view surfaces this as a `"src"` index on every offered item (e.g. `"src": 0`), so
the `VideoStoryEditorAgent` prompt can tell the model which clip each id belongs to and instruct it
to freely alternate between clips across successive `Keep` spans — picking whichever clip has the
best material for each moment is the whole point of offering more than one. The one hard rule: a
single `Keep` span's `FromId` and `ToId` must both come from the SAME clip, since a span is a
contiguous run within one physical file, never a bridge across two files — cross-clip edits are
expressed as a SEQUENCE of single-clip `Keep` spans instead.

Per-source provenance (`VideoAnalysisProvenance`) is aggregated into one artifact-level record via
`AggregateProvenance`: `applied`/`degraded` flags become `true` if ANY source applied/degraded that
stage — an artifact-wide OR, not a per-source breakdown. Full per-source provenance detail is
deliberately out of scope for this addition; a single source passes through unchanged (`Count == 1`
short-circuits).

`VideoAnalysisArtifact.Sources` records one `VideoAnalysisSourceInfo` per analyzed clip, in
source-index order — the storage key `VideoCompileStepExecutor` must download to physically cut
from that clip, and the per-clip `VideoAnalysisMedia` (duration/fps/dimensions) every clip-aware
computation (frame quantization, padding clamps, graphics geometry) must use instead of a single
artifact-wide `Media`. `null` only for a true legacy artifact produced before this field existed —
the one case `VideoCompileStepExecutor` still re-derives the source storage key the old way, by
walking the `VideoAnalyze` step's own config. Every artifact produced by the current executor
populates `Sources` with at least one entry, even for a single source, so the top-level `Media` and
`Sources[0].Media` always agree for that case.

### Compile: which ids can pair, and how the cut is resolved

`VideoCompileStepExecutor` resolves each `Keep` span's `FromId`/`ToId` to `[Start, End)` times plus
the `SourceIndex` recorded against that id — never trusted from the model, since there is no source
field on `VideoEditKeepSpan` for it to get wrong in the first place:

- **`MIXED_SOURCE_SPAN`** — a single span whose `FromId` and `ToId` resolve to different
  `SourceIndex` values fails immediately with this code, before any normalization runs.
- **Ordering/overlap/coalescing** only ever compares a span against the immediately PRECEDING span
  in list order; when that neighbor belongs to a DIFFERENT source clip, there is no shared timeline
  to be "out of order" or "overlapping" on, so the check (and coalescing) is simply skipped at that
  boundary. A single-source config's spans are always same-source neighbors, so this reduces to
  exactly the original single-timeline behavior.
- **Padding/clamping** clamps each span against ITS OWN source clip's duration (`GetSourceMedia`),
  never a single artifact-wide duration.
- **Frame-quantization** uses each span's OWN source clip's exact rational fps, never a single
  artifact-wide fps.
- **Retained ratio** (`Expect.MinRetainedRatio`) sums only the DISTINCT clips actually referenced by
  the resolved cut list, not every clip the step merely analyzed — for a single source this sum has
  exactly one term, so it is byte-identical to before this addition.
- Only the DISTINCT source clips actually referenced by the resolved cut list are downloaded —
  never every clip the `VideoAnalyze` step analyzed.
- The "canonical" clip every multi-source encode normalizes toward (scale/pad/fps for video, sample
  rate/channel layout for audio) and every graphics-geometry computation reads frame dimensions
  from is deliberately the FIRST kept span's own source clip — a deterministic choice independent
  of clip count or offered-id ordering.

**`MULTI_SOURCE_REQUIRES_REENCODE`** — when the resolved cut list references more than one distinct
source clip, `Mode = StreamCopy` is refused as a hard, pre-encode config error (the same discipline
`GRAPHICS_REQUIRE_REENCODE` already established for a different `Reencode`-only combination):
losslessly concatenating independently-encoded files has no correctness-preserving stream-copy
equivalent, since ffmpeg's `concat` filter/demuxer both require matching codec parameters across
inputs that separately-encoded source files are not guaranteed to share, and normalizing them first
is itself a re-encode.

### Why multi-source encoding is a separate method

`EncodeReencodeMultiSourceAsync` is a deliberately separate method from the original
`EncodeReencodeAsync` — which stays completely untouched, and is still used for every single-source
compile, so a single-source (or single-clip-in-practice) compile's ffmpeg argv/behavior stays
byte-identical to before this addition. It is not a generalization of the single-source method
because ffmpeg's `select` filter always emits one input's own matched ranges in THAT INPUT'S OWN
chronological order — it cannot express a `Keep`-span order that jumps between clips arbitrarily.
Only the `concat` filter, fed one small pre-trimmed clip PER SPAN in the exact order they should
play, can. Each span becomes its own `trim`/`atrim` branch off the correct ffmpeg input index for
that span's own source clip, normalized to the canonical frame size/rate/audio format `concat`
requires, then concatenated in `Keep` order.

### Audio-less source clips

Real B-roll/stock footage routinely ships with no audio stream at all. `VideoCompileStepExecutor`
probes every distinct referenced source once (cheap) before encoding and threads the result through
as a per-source `sourceHasAudioByIndex` map:

- If NOT ONE referenced clip has an audio stream, the whole compiled output drops audio entirely —
  there is no real dialogue anywhere to preserve, so adding an all-silent track would add nothing.
  This mirrors the single-source path's own long-standing no-audio-at-all fallback exactly.
- Otherwise (a mix of audio-having and audio-less clips), `concat`'s own stream-count contract
  (every concatenated segment must carry the same `a=` count) is satisfied per span: a span whose
  own source clip has audio gets its real `atrim` branch; a span whose source clip has no audio
  instead gets a synthesized, matching-duration silence branch (ffmpeg's `anullsrc` source filter,
  already natively 48kHz/stereo, so it needs no extra `-i` input or `aformat`). Every span with real
  dialogue keeps its real dialogue — only the audio-less span(s) carry synthesized silence. The
  step's output JSON records which resolved-span indices got synthesized silence
  (`audio.syntheticSilenceSegmentCount`/`syntheticSilenceSegmentIndices`), so this is never a silent
  surprise the way an unreported drop would be.
- Whether the FINAL OUTPUT has any dialogue audio at all (`hasDialogueAudioInOutput` —
  `isMultiSource ? anySourceHasAudio : sourceHasAudio`) also feeds [Background
  music](#background-music)'s ducking decision: there is no point planning silence-gap ducking
  windows against dialogue that will not exist in the output.

---

## Scene/visual analysis (Phase 1)

Phase 1 adds deterministic (no LLM, no new external dependency) visual and audio descriptors per
shot on top of the shipped `VideoAnalyze` step, which until now only knew about cut points
(silence gaps, shot-change timestamps) and speech. It is purely additive: nothing existing changes
behavior when the new config defaults are used, other than the artifact and bounded view gaining
new optional fields (`VideoAnalysisArtifact.Version` moves to `2`, but a `Version: 1` artifact
still deserializes unchanged — every new field is optional/nullable/default-valued and appended,
never inserted or reordered).

### The technique: one low-res raw-frame grid pass

A single new ffmpeg invocation decimates and downscales the source video to a raw RGB pixel grid
file (`FfmpegFrameGridSampler`, `FfmpegArgvBuilder.BuildGridSampleArgs`):

```
ffmpeg -nostdin -hide_banner -y -loglevel error -protocol_whitelist file
       -i {input}
       -an -sn
       -vf fps={sampleFps},scale={gw}:{gh}:flags=area,format=rgb24
       -f rawvideo -pix_fmt rgb24
       {scratch}/grid.rgb
```

`flags=area` is load-bearing — it's a true box-average downscale, so each output pixel is the
exact mean of its source block, which is what makes per-region statistics meaningful. Default grid
is `32x18` at `2.0` fps; `MaxVisualSampleFrames` (default `4000`) clamps the effective fps downward
for very long videos: `effectiveFps = min(VisualSampleFps, MaxVisualSampleFrames / durationSec)`.
Sample time of raw frame `i` is exactly `i / effectiveFps` (the `fps` filter emits CFR from t=0),
so no timestamp parsing is needed anywhere downstream — just byte-offset math
(`frameSizeBytes = gridWidth * gridHeight * 3`).

`FrameGridAnalyzer` (`WorkflowEngine/Services/Video/FrameGridAnalyzer.cs`) is a pure,
unit-testable static class that derives everything below from this one grid buffer — no ffmpeg
stderr scraping for any of it:

- **Motion** — mean absolute luma delta between consecutive sampled frames within a shot,
  normalized 0..1: `MotionMean`/`MotionPeak`/`MotionStdDev`, bucketed into a `MotionClass`
  (`Static`/`Subtle`/`Moderate`/`Dynamic`).
- **Camera move** — a 1-D SAD (sum-of-absolute-differences) integer pixel-shift search
  (`dx`/`dy` in `[-4, 4]`) between consecutive frames' column-sum and row-sum luma profiles. A
  consistent same-sign shift ⇒ `Pan`/`Tilt`; a high-variance alternating-sign shift ⇒ `Handheld`;
  more motion near the frame center than the border ⇒ `Zoom`; near-zero shift and near-zero motion
  ⇒ `Static`; otherwise `Unknown`. Reported as `CameraMove` + `CameraMoveConfidence` (0..1) — this
  is a **documented heuristic, not ground truth**, and is always paired with its confidence.
- **Still windows / head-tail motion** — runs where per-frame motion stays below
  `StillMotionThreshold` for at least `MinStillWindowMs` (capped to `MaxStillWindowsPerShot`,
  longest kept), plus `HeadMotion`/`TailMotion` (mean motion over the shot's first/last 250ms) —
  "will a cut here land mid-motion?"
- **Exposure/color** — Rec.709 luma per pixel, shot-averaged into `BrightnessMean`/
  `BrightnessStdDev` (temporal flicker), `ContrastRms` (intra-frame luma std-dev, shot-averaged),
  `ClippedHighlightRatio`/`CrushedBlackRatio`, `SaturationMean` (HSV). `DominantColors`: the top 3
  bins of a 64-bin (4 levels/channel) RGB histogram, each as a hex color + population share.
- **Regions / safe zones** — the grid's 3x3 spatial cells (`R0`..`R8`) plus three named
  overlay-candidate bands (`LowerThird`, `UpperThird`, `CenterBand`). Per region: `LumaMean`,
  `LumaStdDev` (clutter proxy), `TemporalMotion`, `TextColor` (`Light`/`Dark`, from `LumaMean`),
  and `Suitability` (0..1) — `0.5*clutterScore + 0.3*motionScore + 0.2*extremeScore`, favoring an
  uncluttered (low luma std-dev), low-motion region whose brightness isn't at a 0/1 extreme.
  `BestOverlayRegion` on the shot is the highest-`Suitability` name among the three named bands.
- **Near-duplicate / best-take grouping** — per shot, a signature (`FrameGridAnalyzer.ShotSignature`):
  a z-normalized, time-averaged luma grid (`LumaSig`, 576 floats for 32x18) plus the L1-normalized
  64-bin color histogram (`ColorHist`). `Distance = 0.7*(1-cosine(LumaSig)) + 0.3*(0.5*L1(ColorHist))`,
  `Similarity = 1 - Distance`. Groups form via **single-linkage clustering over a sliding time
  window** (`DuplicateWindowShots`, default 20 — only the previous N shots are compared, both
  faster and more correct since multi-take shots are temporally adjacent) at
  `DuplicateSimilarityThreshold` (default `0.90`). Each group gets an id `d{n}`; members are ranked
  by a heuristic `TakeQuality` (documented in `FrameGridAnalyzer.ComputeTakeQuality`: 35% inverse
  motion jitter, 25% audio level, 20% exposure, 10% duration, 10% neutral sharpness placeholder) —
  rank 0 is `IsBestTake`. A singleton shot gets no group (`DuplicateGroupId = null`).
- **Ken-Burns candidate** — `KenBurnsCandidate = true` when the shot is `Static`, `MotionMean <
  0.02`, duration `>= 2.5s`, and at least one named band has decent `Suitability`; identifies
  candidates only — no zoompan is ever applied (out of scope, as always).
- **Audio levels** — `WavRmsSampler` (pure, `WorkflowEngine/Services/Video/WavRmsSampler.cs`)
  windows the canonical 16kHz mono s16 WAV `FfmpegAudioExtractor` already produces into 250ms
  RMS/peak windows (`20*log10(rms/32768)`, floored at -96 dBFS instead of `-Infinity`); reuses the
  WAV already extracted for transcription, or extracts it fresh if transcription is off/degraded.
  Per shot: `AudioRmsDbfs` (energy-weighted mean of overlapping windows), `AudioPeakDbfs`, and
  `SpeechRatio` (from the already-detected silence spans — no new audio pass), bucketed into a
  `LoudnessClass` (`Quiet`/`Normal`/`Loud`).
- **Not implemented in Phase 1** (opt-in, default `false`, lower priority than the core grid
  pipeline): `DetectLetterbox`/`ActiveCrop` and `DetectSharpness`/`Sharpness` — the config fields
  and artifact columns exist (always `null`/`false`) so a future phase can fill them in without
  another schema migration; `MetadataPrintOutputParser`/`CropDetectOutputParser` (generic
  `ffmpeg ... metadata=print:file=-` / `cropdetect` stderr parsers, mirroring
  `SilenceDetectOutputParser`/`ShowinfoOutputParser`'s style) were likewise left unimplemented.

### `VisualDetail`: degrade before drop

`VideoAnalyzeStepExecutor.BuildBoundedView` treats "how many shots/silences/segments the model
sees" as higher priority than "how much visual/audio detail each one carries". Before ever
dropping an offered item to fit `MaxOutputChars`, it tries the configured `VisualDetail` level,
then each lower level in turn — `Full → Compact → None` — **re-serializing the same set of offered
items** at each level. Only once `None` (which renders a shot exactly as it looked before Phase 1
— no `v`/`a` key at all) still doesn't fit does the pre-existing item-dropping loop run. This
guarantees `meta.offeredIdCount` can never be smaller than what a plain `AnalyzeVisuals: false` run
would produce at the same `MaxOutputChars` — richer per-shot data can only ever cost detail, never
cost coverage. `meta.visual.detail` (and the top-level `meta.visualDetailApplied`) records whichever
level actually got used.

- **`None`** — identical to the pre-Phase-1 shot shape.
- **`Compact`** (default) — `motion`, `move`, `cutIn`/`cutOut`, the single longest still window,
  `bright`, `contrast`, the top 2 dominant colors, `safe` (best overlay region), `dup`/`best`,
  `kenBurns` (only when true), plus `a: {rms, speech}` when audio levels are available.
- **`Full`** — everything `Compact` has, plus the full region list, the full still-window list,
  `motionStdDev`/`motionPeak`/`cameraConfidence`, and all (up to 3) dominant colors.

Every visual/audio number in the view is rounded before serialization — seconds to 2 decimal
places, 0..1 scores to 0..100 integers (`Round2`/`Score` helpers) — since a raw `double` can
serialize as 15-17 characters of floating-point noise, which adds up fast across hundreds of
shots. The four base shot fields (`id`/`startSec`/`endSec`/`durationSec`) are deliberately left
unrounded at every detail level, matching the pre-Phase-1 output exactly.

### View/artifact shape additions

```jsonc
{
  "view": {
    "shots": [{
      "id": "s4", "startSec": 12.4, "endSec": 16.8, "durationSec": 4.4,
      "v": {
        "motion": 12, "move": "Pan", "cutIn": "still", "cutOut": "moving",
        "still": [{ "startSec": 12.4, "endSec": 12.9 }],
        "bright": 41, "contrast": 22, "colors": ["#2b3a4f", "#c9b48a"],
        "safe": { "region": "LowerThird", "fit": 88, "text": "Light" },
        "dup": "d2", "best": true, "kenBurns": true
      },
      "a": { "rms": -21, "speech": 82 }
    }],
    "pacing": { "meanShotSec": 4.1, "medianShotSec": 3.8, "cutsPerMinute": 14.6, "motionTimeline": [12, 30, 8], "timelineBinSec": 5.0 },
    "duplicateGroups": [{ "id": "d2", "shotIds": ["s4", "s7"], "bestShotId": "s7", "similarity": 94 }]
  },
  "meta": {
    "visual": { "applied": true, "degraded": false, "sampleFps": 2.0, "gridWidth": 32, "gridHeight": 18, "detail": "Compact" },
    "audioLevels": { "applied": true }
  }
}
```

`Pacing` (`FrameGridAnalyzer.ComputePacing`) is a whole-artifact summary — `MeanShotSeconds`/
`MedianShotSeconds`/`CutsPerMinute` from shot timing alone, plus a `MotionTimeline` (mean
`MotionMean` per `TimelineBinSeconds`-wide bin, empty when no shot has visual data). `pacing`/
`duplicateGroups` are only surfaced in the view at `Compact`/`Full` detail — never at `None` — so
that a budget-forced collapse to `None` (whether from `AnalyzeVisuals: false` or from a degraded
visual-analysis stage) is always byte-identical regardless of whether visual data merely got
suppressed by degradation vs. never computed at all.

### Failure handling: degrade, never fail the step

Both the visual-analysis block (grid sampling + every `FrameGridAnalyzer` call) and the
audio-level block (WAV sampling) are wrapped in their own try/catch, exactly like the existing
transcription-degrade pattern: on any exception, `Provenance.VisualAnalysisApplied`/
`AudioLevelsApplied` become `false`, `VisualAnalysisDegraded` becomes `true`, a warning is logged,
and the step continues with shots/silences/transcript exactly as if that stage were configured
off. Nothing in Phase 1 can fail a `VideoAnalyze` step.

---

## Vision captioning (Phase 2)

Optional vision-LLM shot captioning on top of Phase 1's deterministic descriptors: extract one
representative keyframe per selected shot, send it to a vision-capable chat model, and get back a
short structured scene description (subjects, action, setting, mood, shot scale, camera angle,
on-screen text, tags). Unlike every other stage in this document, this one is **off by default**
and makes real LLM calls — see [Why `Off`, not `Optional`](#why-off-not-optional) below.

### `InferenceProviderCapability.Vision`

A third `InferenceProvider` capability, alongside `Chat` and `Transcription`. It reuses the
**exact same chat-completions machinery** `Chat` does — `IChatClientFactory`/`ResolvedInferenceProvider`
gained no new members — since a vision call is just an ordinary chat-completions call with an
image content part alongside the text prompt. It is still a separate capability (not folded into
`Chat`) so a vision-capable deployment (which may differ from the deployment an agent's chat
resolution uses) can be configured and defaulted independently: `Vision` participates in its own
"at most one default" bucket via the same composite `(capability, is_default)` partial unique
index `Chat`/`Transcription` already share — no schema migration was needed to add the third
value, since that index is generic over any `capability` value, not hardcoded to two.

`IInferenceProviderResolver.ResolveVisionAsync(explicitProviderId, ct)` mirrors
`ResolveTranscriptionAsync`'s precedence and null-when-nothing-resolves contract exactly
(`explicitProviderId` if it resolves to an enabled `Vision`-capability row → the single enabled
`IsDefault && Capability == Vision` row → `null`), but returns `ResolvedInferenceProvider?` (not a
dedicated vision type) and resolves through the same `ResolveFromProvider` helper `ResolveAsync`
(chat) uses. Like transcription, there is deliberately no fallback to the legacy `AzureOpenAI:*`
config keys — silently sending an image to a deployment that may not support vision would fail
confusingly.

`POST /api/v1/inference-providers/{id}/test` and `POST /api/v1/inference-providers/test` gained a
`Vision` arm alongside the existing `Transcription` arm: it sends a trivial embedded 1x1 JPEG
through `IChatClientFactory` with a "reply with the word ok" prompt and treats any non-empty
response as success — mirroring the existing synthesized-silent-WAV transcription ping. The admin
UI (`InferenceProviderForm`) exposes `Vision` as a third `Capability` option; the per-agent
provider override picker (`AgentInferenceProviderSelect`) filters to `Chat` rows only (an
allowlist, not merely "not Transcription" — a denylist would have silently admitted `Vision` rows
here too), since that override feeds chat resolution only.

### Why `Off`, not `Optional`

`VideoAnalyzeStepConfig.Transcription` defaults to `Optional` because it costs at most **one** ASR
network call per step. Captioning is structurally different: it can cost up to `MaxCaptionedShots`
(default 24) separate vision chat-completion calls — each carrying an image — per `VideoAnalyze`
step. If `Vision` defaulted to `Optional`, then the moment any admin configured a
`Vision`-capability default provider for some unrelated workflow that actually wants captioning,
**every other** existing or future `VideoAnalyze` step in the system would silently start making
real, billed vision calls with no config change of its own. Defaulting to `Off` keeps every step's
cost/latency unchanged unless its author explicitly opts in by setting `Vision` on that step.
`Optional`/`Required` otherwise carry the same degrade-vs-fail semantics `Transcription` does.

### Keyframe selection

`KeyframeSelector` (`WorkflowEngine/Services/Video/KeyframeSelector.cs`) is pure — no ffmpeg, no
I/O:

- **`ChooseKeyframeSec(shot)`** — the midpoint of the shot's longest `StillWindow` (Phase 1) when
  at least one exists (a calm moment makes a cleaner, less motion-blurred frame), else the shot's
  own midpoint. Falls back to the shot midpoint when `Visual` is `null` (visual analysis off,
  degraded, or simply no still windows).
- **`SelectShotsToCaption(shots, duplicateGroups, strategy, maxCaptionedShots, minCaptionShotSeconds)`**
  implements three strategies (`VideoCaptionSelection`), excluding any shot shorter than
  `minCaptionShotSeconds`, and always returning ids in shot-chronological order regardless of
  selection order:
  - **`PerDuplicateGroup`** (default) — captions each near-duplicate group's best-take shot first
    (Phase 1's `DuplicateGroups`), so N takes of one setup cost one vision call, not N; fills any
    remaining budget with the longest not-yet-selected shots.
  - **`LongestShots`** — simply the N longest eligible shots.
  - **`EvenlySpaced`** — shots at roughly even index intervals across the whole shot list.

### Keyframe extraction

`FfmpegArgvBuilder.BuildKeyframeArgs(inputPath, outputJpgPath, atSec, maxWidth)` — a new ffmpeg
invocation alongside Phase 1's grid-sample/audio-extract builders, following the exact same
`IVideoToolRunner` calling convention via a new `IKeyframeExtractor`/`FfmpegKeyframeExtractor` pair
(mirroring `IAudioExtractor`/`FfmpegAudioExtractor`):

```
ffmpeg -nostdin -hide_banner -y -loglevel error -protocol_whitelist file
       -ss {atSec} -i {inputPath}
       -frames:v 1
       -vf scale='min({maxWidth},iw)':-2
       -f image2 -c:v mjpeg -q:v 4
       {outputJpgPath}
```

`-ss` before `-i` for fast input seeking (same convention as `BuildExtractAudioArgs`); the scale
expression never upscales and preserves aspect ratio. `IKeyframeExtractor` is deliberately its own
small interface (not folded into `IFrameGridSampler`) so Phase 3 (motion-graphics overlay
planning, applied at compile time) has a single established pattern to follow for its own new
ffmpeg operations.

### The captioner

`IShotCaptioner`/`VisionShotCaptioner` (`WorkflowEngine/Services/Video/`) build one chat message
per shot — a text prompt plus the keyframe JPEG as a `DataContent("image/jpeg")` content part —
and call `IChatClient.GetResponseAsync` with `ChatResponseFormat.ForJsonSchema<VideoShotCaption>()`,
the exact same structured-output mechanism `AgentStepExecutor`/`ReelForgeAgentBase` use for agent
steps. `IChatClientFactory` is reused unchanged.

```csharp
public sealed record VideoShotCaption(
    string ShotId, string Summary, IReadOnlyList<string> Subjects,
    string Action, string Setting, string Mood,
    string ShotScale, string CameraAngle,
    IReadOnlyList<string> OnScreenText, IReadOnlyList<string> Tags);
```

**Critical safety property:** the shot-id ↔ caption binding is never model-controlled. The model is
called once per shot; `VideoAnalyzeStepExecutor` — never the captioner — always overwrites the
returned `VideoShotCaption.ShotId` with the id it actually requested (`ShotCaptionRequest.ShotId`)
before attaching the caption to a shot. This mirrors the id-anchored discipline
`VideoEditDecisionOutput`/`VideoCompileStepExecutor` already use: never trust an identifier the
model echoes back for anything that matters. Covered by a dedicated test asserting the executor
ignores a deliberately-wrong model-returned `ShotId`.

Captioning retries up to 2 attempts per shot with a short linear backoff (mirrors
`TranscribeWithRetryAsync`'s shape); a captioning failure on one shot never aborts captioning of
the rest — the executor's per-shot loop catches, counts it in `meta.vision.failedShots`, and moves
on.

### Executor wiring

Captioning runs **last** among `VideoAnalyze`'s analysis stages, strictly after every deterministic
stage (silence/shot detection, transcription, Phase 1 visual/audio analysis, near-duplicate
grouping) — so a vision failure can never put anything deterministic at risk:

**Multi-source hoist (Phase 4 fix).** Captioning is a **step-level** pass over every source's shots
combined, run once after all per-source deterministic analysis completes — not a per-source pass
run once per source. This matters for `MaxCaptionedShots`/`VisionTimeoutSeconds`: both are genuinely
step-wide budgets across every source clip, never silently reset per source. An earlier draft of
this feature ran captioning inside the per-source loop, which would have let a 2-source analysis
spend up to `2 × MaxCaptionedShots` vision calls instead of the configured cap — caught and fixed
before merge; `VideoMultiSourceTests` pins the corrected step-wide behavior.

1. `Vision == Off` → skipped entirely, `meta.vision = {mode: "Off", applied: false, ...}`. Every
   other field in the view/artifact is byte-identical to a pre-Phase-2 run — the single most
   important regression test in this phase, mirroring Phase 1's degrade-before-drop test's
   importance.
2. Else, resolve via `ResolveVisionAsync`. `null` + `Required` ⇒ fail with `VISION_UNAVAILABLE`.
   `null` + `Optional` ⇒ degrade (`meta.vision.degraded = true`), continue with no captions.
3. Resolved ⇒ select shots via `KeyframeSelector`, extract each keyframe to scratch, caption each
   with the 2-attempt retry. An aggregate `VisionTimeoutSeconds` wall-clock budget covers the
   *whole* captioning pass (not per-shot) via a linked, timed `CancellationTokenSource`; exceeding
   it mid-pass stops captioning further shots, keeps whatever already succeeded, and sets
   `meta.vision.partial = true`.
4. If zero captions were obtained after all that: `Required` ⇒ fail with `VISION_FAILED`;
   `Optional` ⇒ degrade and continue with zero (or partial) captions.

### View/artifact shape addition

A shot with a caption gains a `"c"` key in the bounded view, gated on `VisualDetail` exactly like
Phase 1's `"v"`/`"a"` keys — same degrade-before-drop discipline, never bypassed:

```jsonc
{
  "view": {
    "shots": [{
      "id": "s4", "startSec": 12.4, "endSec": 16.8, "durationSec": 4.4,
      "c": {
        "summary": "A presenter gestures at a whiteboard while explaining a diagram.",
        "scale": "Medium", "mood": "Focused", "tags": ["presenter", "whiteboard", "explaining"],
        "subjects": ["presenter"], "action": "gesturing at a diagram",
        "setting": "office whiteboard", "cameraAngle": "Eye level", "onScreenText": [],
        "style": "flat ungraded log", "issues": ["soft focus"]
      }
    }]
  },
  "meta": {
    "vision": { "mode": "Optional", "applied": true, "provider": "gpt-4o-mini-vision", "degraded": false, "partial": false, "captionedShots": 6, "failedShots": 0, "persistedKeyframes": 0 }
  }
}
```

`Compact` detail shows `summary`/`scale`/`mood`/`tags`/`style`/`issues` (Phase 4 added `style` and
`issues` at `Compact`, matching Phase 1's own "cheap signals first" discipline); `Full` adds
`subjects`/`action`/`setting`/`cameraAngle`/`onScreenText`/`timeOfDay`/`lighting`/`framing` (Phase
4). A shot with no `"c"` key is normal — not selected for captioning, or captioning off/failed/
degraded — never a signal the shot is empty or unimportant; the `VideoStoryEditor` prompt says so
explicitly.

`VideoAnalysisArtifact.Version` **stays at 2**, not 3: Phase 2 appends exactly one more
optional/nullable field (`VideoAnalysisShot.Caption`) plus optional/default-valued fields on
`VideoAnalysisProvenance`, and a Version-2-without-captions artifact and a
Version-2-with-captions artifact are both valid under the identical shape — no consumer needs to
structurally distinguish them (a consumer that cares simply checks whether `Caption` is null).
Phase 4's five new `VideoShotCaption` fields (`TimeOfDay`, `Lighting`, `VisualStyle`, `Framing`,
`TechnicalIssues`) are additive the same way and do not bump the version either.

### Prompt priming, contact sheets, and persisted keyframes (Phase 4)

Three changes to the captioning path itself, none of which touch the id-anchored/no-timestamp
contract:

- **Prompt priming.** The vision prompt is primed with a short sentence of Phase 1's own
  deterministic measurements for the shot being captioned (color temperature, tone curve,
  saturation, camera move, letterbox/backlit flags) — words derived from measurements, never a
  number the model could restate. The model is told to use this only to inform its reading, not to
  treat it as ground truth it must repeat; `null` (visual analysis off/degraded for that shot) omits
  the block entirely. `ShotCaptionRequest.MeasuredContext` carries this; `VisionShotCaptioner`
  formats and injects it.
- **Contact-sheet keyframes (`KeyframesPerShot`, default `1`).** `1` extracts the same single
  mid-shot still as before Phase 4 — byte-identical. `2` or `3` instead extracts that many frames
  evenly spaced across the shot (`KeyframeSelector.ChooseKeyframeSecs`) and hstacks them into one
  contact-sheet JPEG (`FfmpegArgvBuilder.BuildContactSheetArgs`, `IKeyframeExtractor.ExtractContactSheetAsync`)
  — N cheap input seeks, never a single pass decoding the whole shot. Each pane is scaled to
  `KeyframeMaxWidth / N` so the combined image and its vision-call token cost stay roughly flat. The
  prompt gains one extra sentence telling the model the image is a multi-frame contact sheet of one
  shot, not several shots.
- **`PersistKeyframes`** is now wired: `true` uploads each captioned shot's keyframe JPEG (or
  contact sheet) to storage under `video-analysis/{executionId}/step-{stepOrder}-keyframes/{shotId}.jpg`,
  counted in `meta.vision.persistedKeyframes`. A persist failure is logged and swallowed — it can
  never cost the caption itself, since observability is never allowed to be more load-bearing than
  the thing it observes. Default stays `false` (scratch-only, deleted with the rest of scratch
  space).

Also (judgment call 9): `KeyframeSelector`'s longest-shots fill pass now round-robins its selection
across source clips (grouping candidates by `SourceIndex`, longest-first within each group, then
alternating groups) instead of picking the global longest shots regardless of source — so a
`MaxCaptionedShots` budget spent across several source clips isn't silently exhausted entirely on
one long clip. A single-source analysis produces exactly one round-robin "group", which is provably
identical to the pre-Phase-4 plain longest-first order — no behavior change for the (still
overwhelmingly common) single-source case.

### Explicitly out of scope for Phase 2 and Phase 4

No UI was added to the workflow step-config builder for the `Vision*` fields
(`InferenceProviderForm`'s capability picker and the admin provider table were updated; the
per-step `VideoAnalyze` config panel exposes only the three free Phase 4 switches — see
[Semantic visual dimensions (Phase 4)](#semantic-visual-dimensions-phase-4)) — a workflow author
can still set the `Vision*`/`KeyframesPerShot`/`PersistKeyframes` fields via the raw step JSON
today.

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

## Motion graphics (Phase 3)

Optional motion-graphics overlays — lower-thirds, titles, callouts — applied during
`VideoCompile`'s encode, planned by a third built-in agent that reasons over overlay-safe-zone
"placement" candidates derived deterministically from Phase 1's per-shot region data, but never
emits a timestamp or pixel coordinate — the same structural discipline
`VideoEditDecisionOutput`/`VideoStoryEditorAgent` already established, extended to cover geometry
as well as time. **Off by default** (`VideoAnalyzeStepConfig.EmitOverlayPlacements = false`,
`VideoCompileStepConfig.EnableGraphics = false`) — both flags must be explicitly opted into, and
`EnableGraphics = false` leaves the compile path byte-identical to the pre-Phase-3 behavior.

This is the highest-risk phase of the feature: motion-graphics overlay TEXT is the first
model-authored content in this feature to reach ffmpeg at all (every prior stage passes only
opaque ids and workflow-author-supplied enum/allowlisted config). The text-sanitization and
textfile-based discipline below is the core deliverable of this phase, not polish on top of it.

### Placement candidates (deterministic, in `VideoAnalyze`)

`OverlayPlacementBuilder` (`WorkflowEngine/Services/Video/OverlayPlacementBuilder.cs`) is a pure,
unit-tested static class deriving overlay-placement candidates from shots that already carry Phase
1 `Visual`/`Regions` data (nothing is computed if visual analysis is off or degraded). For each
shot: rank its three NAMED overlay-safe-zone bands — `LowerThird`/`UpperThird`/`CenterBand` (never
the 3x3 `R0`..`R8` grid cells, which exist for other diagnostics, not placement) — by
`Suitability` descending, take up to `MaxPlacementsPerShot`, and resolve a time window:

1. **`LongestStillWindow`** — the shot's longest Phase 1 still window, if one exists (clamped to
   the shot's own bounds).
2. **`ShotMiddle`** — else, a ~2.5s window centered on the shot's midpoint, clamped to the shot's
   own bounds.
3. **`ShotStart`** — else (a degenerate `ShotMiddle` window, e.g. an extremely short shot), a
   window anchored at the shot's start, clamped to the shot's own bounds.

The whole artifact is capped at `MaxPlacements`, dropping the lowest-suitability candidates first;
survivors are then re-sorted into deterministic generation order (shot order, then per-shot rank)
and assigned SEQUENTIAL, globally unique ids `p0`, `p1`, … — a single counter across the whole
artifact, not per-shot.

**Critical id-isolation requirement:** `p{n}` placement ids are a SEPARATE namespace from
`s{n}`/`g{n}`/`t{n}` cut-anchor ids. `VideoAnalysisArtifact.OfferedPlacementIds` is a SEPARATE list
from `OfferedIds`, gated on the exact same "degrade before drop" `VisualDetail` discipline Phase 1
established: `view.placements` is shown as one atomic array at `Compact`/`Full` detail and omitted
entirely at `None` — so a placement id is only ever "offered" when the whole array survived to
whatever detail level the view actually settled on. `VideoCompileStepExecutor.BuildIdTimeIndex`
(which resolves `Keep` span ids to cut times) deliberately does NOT include placements — a code
comment there explains why — and a dedicated test proves a `Keep` span naming a `p0` id fails
`UNKNOWN_ID` exactly like any other id that index does not contain.

`view.placements` entries are deliberately small: `{id, shotId, region, fit, text}` — a
0-100 suitability score and a `"Light"`/`"Dark"` text-color hint, never a rect or a time window
(those are resolved server-side only, at compile time, from the full artifact).

### `AgentType.MotionGraphicsPlanner` and `MotionGraphicsPlanOutput`

An ordinary `StepType.Agent` step — no new step type, exactly the precedent
`AgentType.VideoStoryEditor` set. Given the story editor's decision (or the same bounded view) plus
`view.placements`, it decides zero or more overlays:

```csharp
public class MotionGraphicsOverlay
{
    public string PlacementId { get; set; } = "";   // must be in OfferedPlacementIds
    public string Kind { get; set; } = "";           // LowerThird | Title | Callout | Tag
    public string Text { get; set; } = "";
    public string Subtext { get; set; } = "";
    public string Duration { get; set; } = "";       // Short | Medium | Hold — never a number
    public string Emphasis { get; set; } = "";       // Subtle | Normal | Strong
    public string Reason { get; set; } = "";
}

public class MotionGraphicsPlanOutput
{
    public List<MotionGraphicsOverlay> Overlays { get; set; } = new();
    public string PlanRationale { get; set; } = "";
}
```

Every property on both types is a `string`/`List<string-bearing-type>` — the SAME rushcut
invariant `VideoEditDecisionOutput` established, extended to also forbid a pixel coordinate.
`MotionGraphicsPlanOutputInvariantTests` mirrors `VideoEditDecisionOutputInvariantTests`'s
reflection approach exactly. `Duration`/`Emphasis`/`Kind` are enum WORDS the model chooses from a
closed vocabulary described in its prompt — `VideoCompileStepExecutor` alone resolves `Duration`
to milliseconds (`OverlayShortMs`/`OverlayMediumMs`/`OverlayHoldMs`, defaulting to `Medium` on an
unrecognized value) and `OverlayFontSizePct` to an actual pixel font size, which `Emphasis`
(`Subtle`/`Normal`/`Strong`) then nudges up or down by a fixed multiplier (0.8x/1x/1.25x, still
clamped to the same valid `[2, 12]` percentage range) in `DrawtextFilterBuilder.ComputeFontSize` —
a deliberately small effect, not a whole per-emphasis styling system. `Kind`
(`LowerThird`/`Title`/`Callout`/`Tag`) is currently **descriptive/reserved only** — nothing reads
it downstream, every kind renders identically, since the placement's own region
(LowerThird/UpperThird/CenterBand) already resolves the overlay's geometry and letting `Kind` also
influence position would create two disagreeing sources of geometry for the same overlay. See the
doc comment on `MotionGraphicsOverlay.Kind` in `OutputSchemas.cs`. The
`MotionGraphicsPlannerAgent` class's fallback prompt and the seeded built-in `AgentDefinition` row
in `DatabaseSeeder` are kept verbatim-identical, enforced by the second `[Fact]` in
`VideoStoryEditorPromptConsistencyTests.cs`
(`MotionGraphicsPlanner_fallback_prompt_matches_the_seeded_built_in_agent_prompt_verbatim`,
mirroring the first fact's reflection approach for `VideoStoryEditorAgent`). Tool access is now the
**same full sandbox+Remotion+render pipeline `AuthorAgent` gets, minus `WriteProjectFile`**
(`AgentToolProvider`) — widened from the minimal read-only scope `VideoStoryEditor` gets, since this
agent can optionally back an overlay with a real, rendered Remotion asset (see `RenderedAssetStorageKey`
below) rather than only plain drawtext/drawbox text. This is the only other agent besides `AuthorAgent`
granted `RenderVideoAndUploadToStorage`, and it is a conscious tradeoff: this agent's prompt includes
analysis-view content derived from the source video itself (on-screen text the Phase 2 vision model
read, ASR transcript text), so it is the first agent in this feature with code-execution tools whose
prompt is not limited to user-selected project files. The sandbox's own containment (read-only rootfs,
no network egress by default, no Docker-socket access — see [Security](#security-why-ffmpeg-is-not-in-the-sandbox))
is what bounds the blast radius of a successful prompt injection here: worst case is sandbox-contained
code execution, not host compromise. See `CLAUDE.md`'s "Agent Types (enum)" section for the same note.

### Rendered-asset overlays

An overlay can optionally carry `MotionGraphicsOverlay.RenderedAssetStorageKey` — the S3 storage key
of a transparent-background motion-graphics asset the `MotionGraphicsPlanner` agent produced ITSELF,
by actually calling `RenderVideoAndUploadToStorage` (a real tool call performing a real Remotion
render and a real upload), rather than an id merely echoed back from a set the model was shown. Still
not trusted blindly: `VideoCompileStepExecutor` validates the key matches the exact
`projects/{projectId}/outputFiles/{executionId}/...` prefix `RenderVideoAndUploadToStorage` itself
constructs for the CURRENT execution, before downloading or compositing anything at that key.

When present, `OverlayAssetFilterBuilder` (`WorkflowEngine/Services/Video/OverlayAssetFilterBuilder.cs`)
composites the asset via ffmpeg's `overlay` filter — added as an extra input, stretch-scaled to the
same compact ACCENT box geometry `DrawtextFilterBuilder` computes for a text overlay at the same
placement (`DrawtextFilterBuilder.ComputeAccentBoxPixels`, shared by both builders), and time-shifted
with `setpts` so the asset's own frame 0 lands at the overlay's actual on-screen start time on the
OUTPUT timeline. `eof_action=pass` means once the asset's content runs out the overlay simply stops
contributing (reverts to the plain cut) rather than freezing on its last frame for a longer "Hold"
window. `Text`/`Subtext` are ignored for an overlay that carries a rendered asset — an overlay is one
or the other, never both; a workflow author combines a rendered graphic with separate caption text by
authoring two overlays at different placements. Empty/absent (the default) leaves an overlay a plain
text overlay exactly as before this field existed — additive, not a replacement.

### The source-to-output timeline mapping problem

A placement's window was resolved against the SOURCE video during `VideoAnalyze`. But drawtext's
`enable=`/`alpha=` expressions run against the ffmpeg filtergraph's OUTPUT timeline — the one the
existing select/setpts cut stage produces, which is shorter than the source and has every cut gap
removed entirely. Two internal, directly-unit-tested static functions on
`VideoCompileStepExecutor` solve this purely from the already-resolved, frame-quantized
`ResolvedSpan` list (their `SnappedStart`/`SnappedEnd` — the ACTUAL output-determining times, never
the pre-quantization requested ones):

```csharp
internal static double? MapSourceToOutputSec(IReadOnlyList<ResolvedSpan> spans, double sourceSec);

internal static (double Start, double End)? MapSourceWindowToOutput(
    IReadOnlyList<ResolvedSpan> spans, double startSec, double endSec);
```

Both walk `spans` accumulating output-timeline duration as they go. `MapSourceToOutputSec` returns
`null` when the second falls inside a cut gap (no corresponding output frame exists).
`MapSourceWindowToOutput` intersects a window with the kept spans and returns the output-timeline
window for the FIRST kept portion it overlaps (a single on-screen overlay cannot span a gap in the
output video) — `null` if the window never overlaps any kept span at all. A placement's actual
on-screen source window is `[placement.StartSec, min(placement.StartSec + durationMs/1000,
shot.EndSec)]` — anchored at the placement's start, extended by the model's chosen `Duration`, and
clamped to the OWNING SHOT's own bounds (looked up via `placement.ShotId` against the full
artifact's `Shots`, not merely the placement's own already-narrow window) so a `Hold` duration
cannot overlay past where the shot itself ends.

### Text sanitization and the `textfile=`/`expansion=none` discipline

**This is what makes the existing "not one model-originated character reaches an ffmpeg argv"
claim (see [Security](#security-why-ffmpeg-is-not-in-the-sandbox)) need a qualification, not a
retraction.** Overlay text is model-authored and does reach ffmpeg for the first time in this
feature — but only as sanitized FILE CONTENT, never as argv or filter-string content:

1. **`OverlayTextSanitizer.Sanitize(raw, maxChars)`** (`WorkflowEngine/Services/Video/`) — an
   ALLOWLIST (never a denylist, which is only ever safe against characters someone thought of) of
   letters, digits, combining marks, spaces, and a small safe punctuation set. NFC-normalizes
   first; collapses ALL whitespace (including newlines/tabs — drawtext treats a raw newline as a
   forced line break) to single spaces before the allowlist strips anything, so a newline becomes
   a space rather than being silently deleted (which would wrongly glue two words together);
   truncates to `maxChars` without splitting a grapheme cluster (`StringInfo`-based, never a blind
   `str[..n]`); returns `""` for an empty/whitespace-only result, and the caller then drops that
   overlay/line entirely. **The allowlist is a second, independent layer, not the primary safety
   mechanism** — the primary mechanism is architectural (point 2 below): sanitized text is never
   interpolated into a filter/argv string at all, so no character reaching this far could ever
   terminate a drawtext option or invoke an expansion regardless of what the allowlist admits.
   Given that, the allowlist is kept narrow anyway, as ordinary defense-in-depth: colon (`:`) and
   percent (`%`) are excluded (drawtext's own option separator / expansion syntax), and so are
   `'`, `,`, and `;` (not filter-syntax-significant inside a quoted value, but not essential to a
   lower-third/title/callout either, so excluding them keeps the surface small). Emoji are also
   deliberately excluded (outside `\p{L}`/`\p{N}`/`\p{M}`): the configured overlay font is not
   guaranteed to carry emoji glyphs, so admitting them risks silent tofu-box rendering. Combining
   marks (`\p{M}`) ARE admitted so NFC-normalized text in scripts without precomposed forms (e.g.
   Devanagari vowel signs) survives sanitization instead of being silently mangled
   character-by-character.
2. **Text never appears in the ffmpeg argv or filter string at all.** Each overlay's sanitized
   text/subtext is written to its own scratch file (`{scratch}/ov-{slot}.txt` —
   `DrawtextFilterBuilder.MainTextSlot`/`SubtextSlot` are the single source of truth both the
   writer and the filter-string builder use for slot numbering) and referenced via drawtext's
   `textfile=` option, never an inline `text=` value — so no drawtext metacharacter (`:`, `'`,
   `\`, `%`) in the text can ever terminate or inject into the filter string, because the text
   literally never appears in that string.
3. **`expansion=none` on every drawtext filter** — disables drawtext's own `%{...}` expansion
   syntax (which can read `pts`/`localtime`/`metadata` or run `%{eif:...}` expressions) as
   defense-in-depth, even though the text is already sanitized and file-based.
4. **Every geometry/timing number is computed in C#** (`DrawtextFilterBuilder`, from the probed
   frame size and the already-resolved output-timeline window) and formatted via
   `FfmpegArgvFormat.Number` — culture-invariant, exactly like `EncodeReencodeAsync`'s existing
   `BetweenTerms()` — before being interpolated into the filter string. The model never supplies
   any of this directly; its only contributions are an offered placement id and a handful of
   already-resolved enum words.

`DrawtextFilterBuilder.BuildFilterChain` renames the cut stage's output label from `[vout]` to
`[vcut]` only when there is at least one overlay to draw (when `EnableGraphics=false`, or
`EnableGraphics=true` but zero overlays survived resolution, the label plumbing is untouched — the
cut stage outputs directly to `[vout]` exactly as before Phase 3), then chains one `drawbox`
(semi-transparent background, `enable='between(t,start,end)'`, skipped entirely when
`OverlayBoxColor = "none"`) plus one `drawtext` per overlay (plus a second smaller `drawtext` when
`Subtext` is non-empty), with the LAST overlay's final filter becoming the new `[vout]` that
`-map` continues to reference. `FilterComplexScriptThreshold`'s condition now also checks the
built filter STRING LENGTH (`spans.Count > 64 || filterComplex.Length > 4000`), since overlays can
make one long filter string even with very few cut segments.

### Soft-failure discipline: the cut must never become hostage to graphics

Every graphics-specific failure mode degrades to "no graphics applied", never to a failed compile
— the cut is the primary deliverable:

| Situation | Outcome |
|---|---|
| `GraphicsPlan` configured but unresolvable/invalid JSON | Cut proceeds with no graphics; `graphics.reason` records why |
| `GraphicsPlan` not configured at all (`null`) | Cut proceeds with no graphics; no error |
| An overlay's `PlacementId` not in `OfferedPlacementIds` | That ONE overlay dropped (`unknown_placement_id`); the rest still apply |
| More overlays than `MaxOverlays` | Excess dropped (`max_overlays_exceeded`), in original order |
| Sanitized text ends up empty | That overlay dropped (`empty_text_after_sanitization`) |
| Placement's window falls entirely in a cut gap | That overlay dropped (`cut_away`) |
| `drawtext` filter unavailable in this ffmpeg build | ALL overlays skipped; `graphics.unavailable = true` |
| `EnableGraphics=true` with `Mode=StreamCopy` | Step FAILS `GRAPHICS_REQUIRE_REENCODE` — the one graphics failure that IS a hard failure, since it is a pure config error caught before any resolution work, not a soft runtime condition |

The one exception above (`GRAPHICS_REQUIRE_REENCODE`) is deliberate: drawtext/drawbox filters have
no stream-copy equivalent, so this is a workflow-author config mistake to fix, not a runtime
condition to degrade around.

`VideoCompileStepExecutor.BuildEdl`/the step's output summary JSON gain a `graphics` block —
present ONLY when `EnableGraphics=true` (when `false`, the EDL/output shape is byte-identical to
the pre-Phase-3 compile path):

```jsonc
{
  "graphics": {
    "enabled": true,
    "applied": true,
    "appliedOverlayCount": 1,
    "droppedOverlays": [{ "placementId": "p7", "reason": "unknown_placement_id" }],
    "unavailable": false
  }
}
```

### `drawtext` availability probe

`drawtext` needs libfreetype (Alpine's `font-dejavu` package, which the WorkflowEngine Dockerfile
now installs alongside ffmpeg) and a font file
(`VideoEditingOptions.FontFilePath`, default `/usr/share/fonts/dejavu/DejaVuSans.ttf`, wired
through `VIDEO_FONT_FILE`). A one-off runtime check (`ffmpeg -hide_banner -filters`, checking for
`drawtext` in the output) is cached for the process lifetime — never re-probed per step. If
unavailable (an unrebuilt image, or a dev environment missing the font package), ALL overlays are
skipped and `graphics.unavailable = true` is recorded, but the cut video is still produced
successfully — never fails an otherwise-successful encode over a missing font/filter.

### Not built by Phase 3

Text/box drawtext-drawbox overlays with fade in/out, OR a rendered Remotion asset overlay (see
[Rendered-asset overlays](#rendered-asset-overlays) above) — Phase 3 does NOT apply Ken-Burns
zoompan (Phase 1's `KenBurnsCandidate` remains identification-only), does NOT burn in subtitles, and
does NOT do transitions between cuts. See [Explicitly not built](#explicitly-not-built) below, which
is unchanged by this phase except for graphics moving out of "not built" and into this section.

---

## Background music

An optional background-music bed, mixed under the dialogue during `VideoCompile`'s encode and
ducked automatically during non-speech windows, planned by a fourth built-in agent that picks among
uploaded tracks — the same structural shape [Motion graphics (Phase 3)](#motion-graphics-phase-3)
established: a deterministic candidate list from `VideoAnalyze`, an agent that chooses among opaque
offered ids plus a handful of enum words, and `VideoCompileStepExecutor` alone resolving those words
to real ffmpeg behavior. **Off by default** (`VideoAnalyzeStepConfig.OfferMusicTracks = false`,
`VideoCompileStepConfig.EnableMusic = false`) — `EnableMusic = false` leaves the compile path
byte-identical to the pre-music behavior.

### Why a deterministic volume envelope, not `sidechaincompress`

`MusicMixPlanner`/`MusicMixFilterBuilder` duck the music bed via a **deterministic, keyframed
`volume=eval=frame` envelope** computed from the analysis artifact's own silence gaps/transcript
segments — never a runtime audio-level compressor (`sidechaincompress`). Two reasons this codebase
deliberately does not use a sidechain compressor here:

1. The analysis artifact already carries silence gaps (available with zero dependency on ASR) and
   transcript segments — exactly the physically-grounded "speech has actually stopped" signal
   `VideoCompileStepExecutor.ExtendSegmentEndTowardNextSilence` already trusts over ASR boundaries —
   so there is no need to infer ducking windows from the waveform at encode time at all.
2. A sidechain compressor's behavior depends on the actual waveform at encode time, so nothing could
   assert an exact filter string for it, explain "the music was lifted in these 3 windows" in the
   step's own output JSON, or guarantee sane behavior on a source whose dialogue track already has
   music baked in. The deterministic envelope, by contrast, is something `MusicMixPlanner.PlanLiftWindows`
   decides entirely in C# and `MusicMixFilterBuilder.BuildVolumeExpression` turns into an EXACT,
   assertable ffmpeg filter string.

### Candidate discovery (`VideoAnalyze`)

When `OfferMusicTracks = true`, `VideoAnalyzeStepExecutor` enumerates every `audio/*` project file as
an `m{n}` music-track candidate (`view.musicTracks`), capped by `MaxMusicTracks` (default 20). This
is **project-level, not per-source** — unlike every other candidate list this feature offers, one
candidate list regardless of how many source clips the step analyzed. Candidates are never
ffprobed here — the fit/duration policy is resolved server-side at compile time regardless of a
candidate's exact length, so probing every candidate here would only cost N downloads for a list the
agent picks at most one item from. A `ListFilesAsync` failure degrades to zero candidates; this
never fails the step. `view.musicTracks` is shown as ONE ATOMIC ARRAY, deliberately not gated on
`VisualDetail` (music has nothing to do with visual detail) — it is dropped as a whole array, after
detail has already degraded all the way to `None`, before the per-item drop loop ever runs, mirroring
the discipline [Motion graphics (Phase 3)](#motion-graphics-phase-3) established for `view.placements`.
"Offered" means exactly "the whole `musicTracks` array survived to the final view" —
`VideoAnalysisArtifact.OfferedMusicIds` is empty whenever it was suppressed for budget.

### `m{n}` id isolation

Music-track ids (`m{n}`) are their own namespace, separate from shot/silence/segment ids
(`s{n}`/`g{n}`/`t{n}`), placement ids (`p{n}`), and look-group ids (`k{n}`). `OfferedMusicIds` is its
own separate list — a music-track id must never be validated against `OfferedIds`/
`OfferedPlacementIds` and vice versa — and, like placement/look-group ids, is deliberately NOT
resolvable by `VideoCompileStepExecutor.BuildIdTimeIndex`: a `Keep` span naming an `m{n}` id fails
`UNKNOWN_ID` exactly like any other id that index does not contain.

### `AgentType.MusicSupervisor` and `MusicPlanOutput`

An ordinary `StepType.Agent` step, following the exact precedent `VideoStoryEditor`/
`MotionGraphicsPlanner` set. Given the story editor's decision (or the same bounded view) plus
`view.musicTracks`, it picks AT MOST ONE track plus a few coarse settings:

```csharp
public class MusicPlanOutput
{
    public string TrackId { get; set; } = "";      // must be in OfferedMusicIds; empty = no track suits the edit
    public string Intensity { get; set; } = "";     // Quiet | Balanced | Feature — never a dB number
    public string Ducking { get; set; } = "";       // Off | Light | Normal | Heavy — never a dB number
    public string Fit { get; set; } = "";            // LoopToFit | PlayOnce
    public string Reason { get; set; } = "";
    public string PlanRationale { get; set; } = "";
}
```

The same rushcut invariant extended again: every property is a plain string, so there is no
numeric/time-bearing CLR type to even ban — enforced by `MusicPlanOutputInvariantTests`. The model's
only contributions are an opaque `TrackId` drawn from the set it was actually offered plus the three
enum-word choices; `VideoCompileStepExecutor` alone resolves those to dB levels/ffmpeg behavior. This
agent is entirely optional: `VideoCompileStepConfig.MusicTrackProjectFileId`, set directly by the
workflow author, delivers the whole capability (a fixed track, default settings) without this agent
at all. Same minimal read-only project-context + `FailWorkflow` tool scope as `VideoStoryEditor` — no
render/sandbox escape hatch the way `MotionGraphicsPlanner` has, since there is no media for this
agent to produce itself.

### Resolution: `ResolveMusicAsync`, soft-failure throughout

`VideoCompileStepExecutor.ResolveMusicAsync` runs only when `EnableMusic = true`, and — like every
other stage in this feature — never fails the compile, only degrades to "no music applied":

| Situation | Outcome |
|---|---|
| `MusicPlan` configured but unresolvable, or not valid JSON | Dropped (`plan_unresolved` / `plan_invalid_json`); falls through to `MusicTrackProjectFileId` if set, else no music |
| Plan's `TrackId` not in `OfferedMusicIds`, or names no known candidate | Dropped (`unknown_track_id`); same fallthrough |
| Resolved project file not found in the project, or not an `audio/*` mime type | Dropped (`track_not_in_project` / `track_not_audio`); no music |
| Track download or ffprobe fails, or reports zero duration/no audio stream | Dropped (`track_download_or_probe_failed`); no music |
| This ffmpeg build's `amix` filter has no `normalize` option | `music.unavailable = true`; no music (the cut still succeeds) |

The `amix` normalize-option probe (`IsAmixNormalizeAvailableAsync`) is cached for the process
lifetime, mirroring `IsDrawtextAvailableAsync`'s pattern exactly — `normalize=0` is load-bearing, not
cosmetic: without it `amix` silently halves every input's level, including the dialogue track, so a
missing option must degrade ALL music rather than risk quietly reducing dialogue loudness.

Resolution order for which track plays: `MusicPlan` (the agent's choice) first, then
`MusicTrackProjectFileId` (the deterministic workflow-author-configured fallback) if the plan did not
resolve to a usable track, else no music at all.

### Enum words to ffmpeg behavior

`Intensity`/`Ducking`/`Fit` are resolved entirely server-side, exactly mirroring how
[Motion graphics](#motion-graphics-phase-3) resolves `Duration`/`Emphasis`:

- **`Intensity`** (`Quiet`/`Balanced`/`Feature`) → bed level in dBFS via `MusicBedQuietDb`/
  `MusicBedBalancedDb`/`MusicBedFeatureDb` (defaults `-26`/`-20`/`-14`), clamped `[-40, -6]`.
- **`Ducking`** (`Off`/`Light`/`Normal`/`Heavy`) → attenuation below the bed while dialogue is
  present, via `MusicDuckLightDb`/`MusicDuckNormalDb`/`MusicDuckHeavyDb` (defaults `-6`/`-11`/
  `-18`), clamped `[-30, 0]`. `Off` (from either the model or `VideoCompileStepConfig.MusicDucking =
  MusicDuckingMode.Off`) collapses lift-window planning entirely — a flat ducked bed throughout, no
  trapezoid expression.
- **`Fit`** (`LoopToFit`/`PlayOnce`) → `LoopToFit` adds `-stream_loop -1` to the music input and
  trims to the edit's exact frame-quantized length; `PlayOnce` trims to the track's own length when
  shorter than the edit, no loop.

### `MusicMixPlanner`: where to lift the bed

`MusicMixPlanner.PlanLiftWindows` (pure, no I/O, no ffmpeg — the `OverlayPlacementBuilder` precedent
for this feature's other deterministic planning code) decides WHERE, on the compiled edit's own
OUTPUT timeline, the bed should rise back toward its unducked level. Basis selection, in order — the
first one with any data wins:

1. **`silenceGaps`** (preferred) — the artifact's own detected silence spans, mapped through the same
   `MapSourceWindowToOutput` helper [Motion graphics](#motion-graphics-phase-3) uses for placement
   windows.
2. **`speechComplement`** — else, the gaps BETWEEN transcript segments, computed per source clip.
3. **`noSpeechDetected`** — else, a single lift window spanning the WHOLE output: there is no
   dialogue anywhere in the kept edit, so the bed should not be needlessly ducked for the whole
   video.

Candidate windows are merged (closer together than `MusicLiftMergeMs`), dropped below `MinMusicLiftWindowMs`
(and always below twice the gain ramp, so a lift too short to fully ramp never reads as pumping), and
capped at `MaxMusicLiftWindows` (longest kept, re-sorted chronologically).

`MusicMixFilterBuilder.BuildVolumeExpression` turns the plan into an exact `volume=eval=frame`
expression: zero lift windows collapse to the bare ducked-gain constant; one window is a single
trapezoid (0 outside `[start, end]`, ramping linearly to 1 across `MusicDuckRampMs` INSIDE each end of
the window, so a lift is never above the ducked level exactly at a boundary); more than one window
nests binary `max(...)` calls (ffmpeg's `eval` has no n-ary max). `BuildMixStage`'s final `amix` uses
`normalize=0` (see above), `duration=first` (pins the mixed output's length to the DIALOGUE input, so
a looped/infinite music input can never extend the file), and `dropout_transition=0` (avoids a gain
re-ramp when the music branch ends before the dialogue does, for `PlayOnce` with a track shorter than
the edit).

### Skipped entirely when the output has no dialogue audio

When the compiled output has no dialogue audio at all — a single audio-less source clip, or (in a
[multi-source](#multiple-source-clips) compile) a mix where NOT ONE referenced clip has an audio
stream — there is nothing to duck against. `VideoCompileStepExecutor` computes
`hasDialogueAudioInOutput` (`isMultiSource ? anySourceHasAudio : sourceHasAudio`) once, after source
download/probe, and threads it into `ResolveMusicAsync` as `hasDialogueAudio`. That flag folds into
the SAME `duckingOff` switch that already collapses lift-window planning to a flat, undocked bed
level (`duckingOff = configDuckingOff || !hasDialogueAudio`) — so silence-gap/speech-complement
ducking windows are never planned against dialogue that will not exist in the output. `music.duckBasis`
records `"no_dialogue_audio"` explicitly in this case (distinct from `"none"`, which means ducking was
simply turned off by config/the model while dialogue audio does exist), and
[`dialogueHeadroom`](#review-evidence-dialogueheadroom) reports `{"applicable": false, "reason":
"no_dialogue_audio_in_output"}` instead of computing a headroom number against Phase 1 loudness data
for audio that was dropped from the output entirely. The music bed itself is unaffected by this — a
track can still be mixed in as the entire soundtrack of an otherwise-silent edit; only the
speech-aware ducking behavior is skipped.

### Review evidence: `dialogueHeadroom`

`VideoCompileStepExecutor.BuildDialogueHeadroom` computes deterministic evidence for
`AgentType.VideoReviewAgent`'s `StepType.ReviewLoop` step: the duration-weighted mean dialogue RMS
across the KEPT spans only (from Phase 1's per-shot `VideoAnalysisShotAudio.RmsDbfs`) against the
resolved ducked-music level — a hard, server-computed headroom number, not something a model
estimates from audio it cannot hear:

```jsonc
{ "applicable": true, "meanDialogueRmsDbfs": -22.4, "duckedMusicDbfs": -31.0, "headroomDb": 8.6 }
```

`applicable: false` when no kept shot carries a Phase 1 audio descriptor (`AnalyzeAudioLevels` was
off/degraded) or, per the previous section, when the output has no dialogue audio at all.

### EDL / output JSON shape

Present only when `EnableMusic = true` (byte-identical to the pre-music compile path otherwise):

```jsonc
{
  "music": {
    "enabled": true, "applied": true, "unavailable": false,
    "source": "plan", "trackId": "m1", "trackName": "ambient-bed.mp3",
    "intensity": "Balanced", "ducking": "Normal", "fit": "LoopToFit",
    "bedDbfs": -20, "duckedDbfs": -31,
    "trackDurationSec": 42.0, "outputDurationSec": 96.3, "loops": 3,
    "playEndSec": 96.3, "fadeInSec": 1.5, "fadeOutSec": 2.5,
    "duckBasis": "silenceGaps", "liftWindows": 4, "liftCoveragePct": 18.2, "speechCoveragePct": 71.4,
    "dialogueHeadroom": { "applicable": true, "meanDialogueRmsDbfs": -22.4, "duckedMusicDbfs": -31.0, "headroomDb": 8.6 },
    "dropped": []
  }
}
```

`source` is `"none"` / `"plan"` / `"config"` (which of `MusicPlan`/`MusicTrackProjectFileId`
actually supplied the track); `dropped` is a list of `{reason, trackId}` entries recording every
soft-failure the table above allows, empty when music applied cleanly.

---

## Semantic visual dimensions (Phase 4)

Seven deterministic dimensions (D1-D7) added on top of Phase 1's scene/visual analysis, plus
prompt/contact-sheet/persistence changes to Phase 2's vision captioning. D1-D4 and D6 are **free**
— derived from the same low-res grid data Phase 1 already samples, so they default **on**. D5
(audio character) rides `AnalyzeAudioLevels` the same way. D7 (backlit) is a byproduct of D6's
region data, also free. Only sharpness (a D-adjacent dimension, not part of D1-D7's own numbering)
costs a genuinely new ffmpeg invocation per measured shot, so it alone stays opt-in.

Every new stage below follows Phase 1's original discipline: **degrade, never fail the step.** A
classification that cannot be computed (near-monochrome frame, too few audio windows, no clean
letterbox bars) simply omits that field or id — it never throws out of the analysis pass, and it
never blocks silence/shot detection, transcription, or any other deterministic stage from
completing.

| # | Dimension | Formula (informal) | Gate | Documented limitation |
|---|---|---|---|---|
| D1 | Color temperature (`Warmth`/`Tint`/`ColorTemperatureClass`) | `Warmth = clamp((meanR − meanB) / 0.25, −1, 1)`; `Tint` is the same shape against G vs. (R+B)/2. `ColorTemperatureClass` is `Warm`/`Cool` at `\|Warmth\| ≥ 0.20`, else `Neutral`; a near-monochrome frame (`SaturationMean < 0.05`) is always `Neutral` regardless of `Warmth` | `AnalyzeColorGrading` | A single dominant colored object (not the lighting) can skew the whole-frame mean; this is a frame-average heuristic, not a white-balance measurement |
| D2 | Tone curve (`BlackPoint`/`WhitePoint`/`ToneClass`) | 5th/95th percentile of the luma histogram (nearest-rank). `ToneClass` order is load-bearing — an actual exposure defect always outranks a stylistic read: `Blown` (clipped highlight ratio > 5%) → `Crushed` (crushed black ratio > 5%) → `Flat` (dynamic range < 0.45 **and** black point > 0.10 — lifted blacks + compressed range, i.e. log/ungraded) → `Contrasty` (dynamic range > 0.75 **and** black point < 0.06) → `Normal` | `AnalyzeColorGrading` | `Flat` on a whole look group is a property of the SOURCE footage (ungraded log), not a per-shot defect — both agent prompts say so explicitly |
| D3 | Saturation character (`SaturationClass`) | Pure threshold projection of the pre-existing `SaturationMean`: `Muted` (< 0.18) / `Natural` / `Vivid` (> 0.42) | `AnalyzeColorGrading` | Same mean-based coarseness as D1 |
| D4 | Look grouping (`view.lookGroups`, ids `k{n}`) | Six-float `LookSignature` (`Warmth`, `Tint`, `BrightnessMean`, `BlackPoint`, `WhitePoint`, `SaturationMean`) per shot; `LookDistance` is a weighted L1 distance normalized per-component to 0..1 (weights `0.30/0.10/0.25/0.15/0.10/0.10`, summing to 1.0 so `Similarity = 1 − Distance` lands in 0..1); single-linkage clustering over **all pairs** (not windowed like near-duplicate grouping, since a shared look deliberately links non-adjacent shots/clips) at `LookSimilarityThreshold` (default `0.88`). `LookRank` orders group members by ascending distance to the group centroid — rank 0 is the most representative shot | `DetectLookGroups` | O(shots²) — trivially cheap at the shot counts this feature targets, but would need revisiting at extreme shot counts |
| D5 | Audio character (`char` under `"a"`) | `ShotAudioAnalyzer.Analyze`: crest factor (peak − RMS dB), 10th-percentile window RMS as a noise floor, level stability (`1 − clamp(stdDev/12dB, 0, 1)`), and zero-crossing rate. Classification order is load-bearing (`Dialogue` is the fallthrough, never a positive claim): `Silent` (RMS ≤ −50 dBFS) → `Music` (stable + compressed + low ZCR) → `Noisy` (high noise floor, low speech ratio) → `Ambient` (quiet, low speech ratio) → `Dialogue` | `AnalyzeAudioLevels` | Deliberately a ZCR/crest/noise-floor heuristic, not a spectral (FFT) classifier — this repo's only audio test fixtures are synthetic sine tones, and thresholds tuned against a 440 Hz sine would pass CI while misclassifying real footage (judgment call 7) |
| D6 | Letterbox/pillarbox (`ActiveCrop`) | Scans the already-materialized luma frames for rows/columns whose luma stays below a tolerant black threshold (16/255, tolerant of compression noise inside a true matte) across every sampled frame, capped at 40% of the frame dimension (beyond that it reads as a dark scene, not bars) | `DetectLetterbox` (default **true** — free, no second luma pass) | Under-reports soft/gradient letterbox edges — the threshold expects a clean black bar, not a feathered one |
| D7 | Backlit candidate (`BacklitCandidate`) | Byproduct of D6's region data: the center region (`R4`) is markedly darker than the average of the surrounding border regions (`borderLuma − centerLuma > 0.18`) while itself being dark (`centerLuma < 0.35`) | Free whenever regions are computed | Named and surfaced as a *candidate*, not a claim (mirrors `KenBurnsCandidate`'s precedent) — fed to the vision prompt so a model that can actually see the frame turns it into (or rejects) an actual assessment; `Full` detail only |
| — | Sharpness (`Sharpness`, D-adjacent) | Native-resolution, square, centered grayscale patch (`FfmpegArgvBuilder.BuildSharpnessPatchArgs`, deliberately its own ffmpeg invocation so a fault there can never take Phase 2 keyframe extraction down with it) → discrete 4-neighbour Laplacian → variance over the interior, normalized against a documented heuristic constant. Only ever compared BETWEEN shots of the same source, never as an absolute unit | `DetectSharpness` (default **false** — one extra ffmpeg call per measured shot) | Costs real wall-clock/CPU unlike D1-D4/D6/D7, which is why it alone stays opt-in; capped by `MaxSharpnessShots`, a genuinely step-wide budget (like `MaxCaptionedShots`) computed AFTER the per-shot analyze loop and BEFORE that source's own duplicate grouping, so a real measured value (when available) — not the neutral placeholder — reaches `ComputeTakeQuality`'s best-take scoring |

### `k{n}` isolation

Look-group ids (`k{n}`) are a purely **descriptive** namespace, structurally different from every
other id this feature offers a model. Shot/silence/segment ids (`s{n}`/`g{n}`/`t{n}`) are offered to
`VideoStoryEditorAgent` and resolved by `VideoCompileStepExecutor.BuildIdTimeIndex`; placement ids
(`p{n}`) and music-track ids (`m{n}`) are separate offered namespaces resolved by their own
executor paths. `k{n}` is **never offered** to any agent at all — there is no `OfferedLookIds` list,
deliberately — and no structured output in this feature (`VideoEditDecisionOutput`,
`MotionGraphicsPlanOutput`, `MusicPlanOutput`) has a field that can name a look group. A `Keep` span
naming `k{n}` can therefore only be a hallucination, and `BuildIdTimeIndex` deliberately excludes
`k{n}` from its index so such a span fails `UNKNOWN_ID` exactly like any other id it does not
contain — the same fate as a `p{n}`/`m{n}` id named in a `Keep` span.

### View/artifact shape

```jsonc
{
  "view": {
    "shots": [
      {
        "id": "s0", "startSec": 0.0, "endSec": 4.2, "durationSec": 4.2, "src": 0,
        "v": {
          "motion": 12, "move": "Static", "cutIn": "still", "cutOut": "still",
          "bright": 58, "colors": ["#3a2c1e", "#c9a876"],
          "temp": "Warm", "tone": "Normal", "sat": "Natural", "look": "k0", "crop": [0.0, 0.11, 1.0, 0.78]
        },
        "a": { "rms": 42, "speech": 71, "char": "Dialogue" },
        "c": {
          "summary": "A presenter gestures at a whiteboard while explaining a diagram.",
          "scale": "Medium", "mood": "Focused", "tags": ["presenter", "whiteboard"],
          "style": "flat ungraded log", "issues": []
        }
      }
    ],
    "lookGroups": [
      { "id": "k0", "shotIds": ["s0", "s1", "s4"], "repShotId": "s0", "cohesion": 91, "temp": "Warm", "tone": "Normal", "sat": "Natural" }
    ]
  },
  "meta": {
    "look": { "applied": true, "uniform": false, "groupCount": 1 },
    "vision": { "mode": "Optional", "applied": true, "captionedShots": 6, "failedShots": 0, "persistedKeyframes": 0 }
  }
}
```

`"temp"`/`"tone"`/`"sat"` and `"look"` live under a shot's existing `"v"` node — gated on
`AnalyzeColorGrading`/`DetectLookGroups` and `VisualDetail` exactly like every other Phase 1 field,
same degrade-before-drop discipline. `"crop"` (D6) appears only when a crop was actually detected —
omitted, not `null`, when the frame is full-bleed. `"char"` (D5) is always present on the `"a"` node
whenever audio levels were analyzed, including for the common `Dialogue` value — unlike the visual
fields, there is no "absent means nothing to report" reading for D5, since every shot has SOME
audio character. `view.lookGroups` is capped by `MaxViewLookGroups`; when the whole analysis is one
uniform look, every per-shot `"look"` id is suppressed and `meta.look.uniform` is `true` instead —
repeating the same group id on every shot would add bytes without adding information.

### Progress weighting

`VideoAnalyzeProgressPlan`'s per-source stage list gained `SampleSharpness` (weight `4`, only
counted when `DetectSharpness` is on) between `AnalyzeShots` and `GroupDuplicates`, and the
step-level list gained `MatchLooks` (weight `2`, only counted when `DetectLookGroups` is on) between
`GroupDuplicates`/multi-source join and `ListMusicCandidates`:

| Stage | Weight | Level |
|---|---|---|
| `SampleFrameGrid` | 12 | Per-source |
| `AnalyzeShots` | 10 | Per-source |
| `SampleSharpness` | 4 | Per-source (only when `DetectSharpness`) |
| `GroupDuplicates` | 2 | Per-source |
| `MatchLooks` | 2 | Step-level (only when `DetectLookGroups`) |
| `CaptionShots` | 14 | Step-level |

A disabled stage contributes zero weight and is skipped entirely rather than reported as an
instant 0%-to-100% jump — the same "only enabled stages count toward the total" rule the original
plan established for every other optional stage, so turning a Phase 4 dimension off never distorts
the percentages reported for the stages that stayed on.

### Vision-phase changes

See [Prompt priming, contact sheets, and persisted keyframes (Phase 4)](#prompt-priming-contact-sheets-and-persisted-keyframes-phase-4)
under Vision captioning, and the multi-source hoist fix noted at the top of
[Executor wiring](#executor-wiring) — both are Phase 4 changes to the Phase 2 captioning path,
documented alongside Phase 2 rather than duplicated here.

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

> **Qualification (Phase 3):** motion-graphics overlay TEXT is model-authored and does reach
> ffmpeg — but never as argv or filter-string content. It is sanitized to an allowlist, written to
> its own scratch file, and referenced only via drawtext's `textfile=` option (with
> `expansion=none` set as defense-in-depth). See
> [Motion graphics (Phase 3)](#motion-graphics-phase-3) for the full discipline. The claim above —
> "not one model-originated character reaches an ffmpeg argv" — still holds exactly as stated for
> the argv/filter-string surface; it is the reason Phase 3's text still cannot inject into it.

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
spans within one clip); no arbitrary unanalyzed asset/image insertion (cutting across several
pre-declared, analyzed `Sources` clips — including B-roll — is supported, see
[Multiple source clips](#multiple-source-clips), but inserting an image or a clip that was never
fed in as a `Source` is not); no multicam (no automatic multi-angle sync/switching); no
picture-in-picture; no speed ramps; no transitions between cuts (hard cuts only).

**Post scope:** no colour grading / LUTs / filters / stabilization (Phase 4's D1-D3 color
dimensions are measured/reported only, same as `loudnorm` below, never applied); no loudness
normalization (`loudnorm` is measured and reported only, never applied); no subtitle burn-in and no
SRT/VTT export (the transcript exists, so this is the most obvious phase-2 add); no speaker
diarization. Background music IS built — see [Background music](#background-music) — but it is a
deterministic bed/ducking mix only: no auto-composed score, no beat-matching to cuts, no per-section
music cues.

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
