namespace ReelForge.Shared.Workflows;

/// <summary>
/// Controls whether/how a <c>VideoAnalyze</c> step attempts ASR transcription.
/// <c>Off</c> is deterministic and has no external dependency; <c>Optional</c> (the default)
/// degrades cleanly to <c>Off</c> when no transcription-capable provider resolves or the call
/// fails after bounded retry; <c>Required</c> fails the step with a precise diagnostic instead
/// of silently shipping an edit decision with no transcript context.
/// </summary>
public enum VideoTranscriptionMode
{
    Off,
    Optional,
    Required
}

/// <summary>
/// How much per-shot visual/audio detail (Phase 1 descriptors) the bounded prompt view includes
/// per offered shot. <c>VideoAnalyzeStepExecutor.BuildBoundedView</c> degrades through these
/// levels (Full → Compact → None) BEFORE ever dropping an offered item to fit
/// <see cref="VideoAnalyzeStepConfig.MaxOutputChars"/> — richer per-shot data must never silently
/// reduce how many shots/silences/segments the story-editor agent gets to see.
/// </summary>
public enum VideoVisualDetail
{
    /// <summary>No <c>v</c>/<c>a</c> key on any shot — identical to today's (pre-Phase-1) view shape.</summary>
    None,

    /// <summary>A small, LLM-budget-friendly summary per shot (motion class, camera move, cut-in/out, dup/best, bright, colors, rms/speech).</summary>
    Compact,

    /// <summary>Everything <c>Compact</c> has, plus the full region list, full still-window list, and motion std-dev/peak.</summary>
    Full
}

/// <summary>
/// Cheap, in-process structural checks run against the produced analysis before the step is
/// considered successful. No model call. Mirrors <see cref="ExtractExpectation"/>'s role for
/// Extract steps.
/// </summary>
public sealed record VideoAnalyzeExpectation(
    int? MinShots = null,
    int? MinTranscriptSegments = null,
    double? MaxSilenceRatio = null,
    int? MinShotsWithVisuals = null);

/// <summary>
/// JSON configuration for a <c>StepType.VideoAnalyze</c> workflow step. Deserialized from
/// <c>WorkflowStep.VideoAnalyzeConfigJson</c>. A closed, versioned, strongly-typed record —
/// the same house style as <see cref="ExtractStepConfig"/>: deterministic, non-LLM, and never
/// throws (every failure mode is representable in the resulting <c>{view, meta}</c> envelope
/// rather than as an exception).
///
/// <para>
/// Phase 1 ("scene/visual analysis") appends the <c>Analyze*</c>/<c>Visual*</c>/<c>Duplicate*</c>
/// fields below the original field set (all with defaults) so existing persisted/template configs
/// keep deserializing unchanged; <see cref="Expect"/> stays the LAST positional parameter.
/// </para>
/// </summary>
public sealed record VideoAnalyzeStepConfig(
    int Version,
    VideoSourceRef Source,
    // -- silence detection --
    bool DetectSilence = true,
    double SilenceThresholdDb = -34.0,
    int MinSilenceMs = 350,
    // -- shot/scene detection --
    bool DetectShots = true,
    double SceneThreshold = 0.30,
    // -- transcription --
    VideoTranscriptionMode Transcription = VideoTranscriptionMode.Optional,
    Guid? TranscriptionProviderId = null,
    string? Language = null,
    bool WordTimestamps = true,
    int MaxAsrChunkBytes = 20_000_000,
    // -- guardrails, checked BEFORE any decode --
    int MaxDurationSeconds = 1800,
    long MaxInputBytes = 2_000_000_000,
    // -- prompt-view budget (mirrors ExtractStepConfig.MaxOutputChars: drop whole trailing
    //    items and re-serialize, never truncate mid-JSON) --
    int MaxOutputChars = 24_000,
    int MaxViewSegments = 400,
    int MaxSegmentTextChars = 160,
    // -- Phase 1: visual scene analysis (one low-res raw-frame grid ffmpeg pass + pure C#) --
    bool AnalyzeVisuals = true,
    double VisualSampleFps = 2.0,
    int VisualGridWidth = 32,
    int VisualGridHeight = 18,
    /// <summary>
    /// Clamps the effective sample fps downward for very long videos:
    /// <c>effectiveFps = min(VisualSampleFps, MaxVisualSampleFrames / durationSec)</c>.
    /// </summary>
    int MaxVisualSampleFrames = 4000,
    double StillMotionThreshold = 0.02,
    int MinStillWindowMs = 400,
    int MaxStillWindowsPerShot = 3,
    /// <summary>Opt-in, lower priority than the core grid pipeline — see docs/video-editing.md.</summary>
    bool DetectLetterbox = false,
    /// <summary>Opt-in, lower priority than the core grid pipeline — see docs/video-editing.md.</summary>
    bool DetectSharpness = false,
    // -- Phase 1: audio loudness (reuses the WAV already extracted for transcription, or
    //    extracts it if transcription is off) --
    bool AnalyzeAudioLevels = true,
    // -- Phase 1: near-duplicate / best-take grouping --
    bool DetectNearDuplicates = true,
    double DuplicateSimilarityThreshold = 0.90,
    int DuplicateWindowShots = 20,
    // -- Phase 1: bounded-view detail level, and the degrade-before-drop budget it governs --
    VideoVisualDetail VisualDetail = VideoVisualDetail.Compact,
    int MaxViewDuplicateGroups = 20,
    VideoAnalyzeExpectation? Expect = null);
