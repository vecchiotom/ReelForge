namespace ReelForge.Shared.Workflows;

/// <summary>
/// The full, non-truncated video analysis document — the source of truth persisted to
/// <c>projects/{projectId}/agentFiles/video-analysis/{executionId}/step-{order}-analysis.json</c>
/// and referenced by <c>WorkflowStepResult.ArtifactStorageKey</c>. The bounded <c>{view, meta}</c>
/// prompt envelope handed to the story-editor agent is a trimmed projection of this document.
/// </summary>
/// <remarks>
/// Ids are assigned here, deterministically by index (<c>s{n}</c> shots, <c>g{n}</c> silence
/// gaps, <c>t{n}</c> transcript segments, <c>w{n}</c> words), so a truncated prompt view's ids
/// still resolve back to this artifact unambiguously. <see cref="OfferedIds"/> records exactly
/// which ids made it into the prompt view that was actually shown to the model — the compile
/// step must reject any id that is merely present in this artifact but was never offered
/// (see plan §4.3, "not merely in the artifact — in the set actually shown to the model").
/// </remarks>
public sealed record VideoAnalysisArtifact(
    int Version,
    VideoAnalysisMedia Media,
    IReadOnlyList<VideoAnalysisShot> Shots,
    IReadOnlyList<VideoAnalysisSilenceSpan> SilenceSpans,
    IReadOnlyList<VideoAnalysisSegment> Segments,
    IReadOnlyList<VideoAnalysisWord> Words,
    IReadOnlyList<string> OfferedIds,
    VideoAnalysisProvenance Provenance);

/// <summary>
/// Probed media characteristics. Fps is carried as an exact rational (ffprobe's
/// <c>r_frame_rate</c>, e.g. 30000/1001) rather than a single double, so downstream frame-exact
/// arithmetic never accumulates floating-point drift.
/// </summary>
public sealed record VideoAnalysisMedia(
    double DurationSec,
    int FpsNum,
    int FpsDen,
    int Width,
    int Height);

/// <summary>A detected shot/scene. Id: <c>s{n}</c>, assigned by index in scene-detection order.</summary>
public sealed record VideoAnalysisShot(
    string Id,
    double StartSec,
    double EndSec);

/// <summary>
/// A detected silence gap. Id: <c>g{n}</c>. <see cref="AfterShot"/> links each gap to the shot
/// it trails, purely to make the bounded prompt view legible — it carries no execution meaning.
/// </summary>
public sealed record VideoAnalysisSilenceSpan(
    string Id,
    double StartSec,
    double EndSec,
    string? AfterShot = null);

/// <summary>
/// A transcript segment. Id: <c>t{n}</c>. <see cref="Shot"/> links each segment back to the shot
/// it falls within, again only to aid the prompt view.
/// </summary>
public sealed record VideoAnalysisSegment(
    string Id,
    string? Shot,
    double StartSec,
    double EndSec,
    string Text);

/// <summary>
/// A single transcribed word. Id: <c>w{n}</c>. Persisted in the full artifact for audit and as
/// the phase-2 basis for subtitle export, but never offered to the model directly in v1 — the
/// segment is the offered granularity (see plan §8, "Explicitly NOT in this iteration").
/// </summary>
public sealed record VideoAnalysisWord(
    string Id,
    double StartSec,
    double EndSec,
    string Text);

/// <summary>
/// Records how this artifact was produced, for audit and as the basis for the
/// <c>meta.transcription</c> block surfaced in the bounded prompt view (including the
/// "degraded" signal when <see cref="VideoTranscriptionMode.Optional"/> fell back to no ASR).
/// </summary>
public sealed record VideoAnalysisProvenance(
    VideoTranscriptionMode TranscriptionMode,
    bool TranscriptionApplied,
    bool TranscriptionDegraded,
    string? TranscriptionProvider = null,
    string? TranscriptionLanguage = null,
    DateTime? AnalyzedAt = null);
