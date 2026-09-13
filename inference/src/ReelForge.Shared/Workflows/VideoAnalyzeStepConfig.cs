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
/// Cheap, in-process structural checks run against the produced analysis before the step is
/// considered successful. No model call. Mirrors <see cref="ExtractExpectation"/>'s role for
/// Extract steps.
/// </summary>
public sealed record VideoAnalyzeExpectation(
    int? MinShots = null,
    int? MinTranscriptSegments = null,
    double? MaxSilenceRatio = null);

/// <summary>
/// JSON configuration for a <c>StepType.VideoAnalyze</c> workflow step. Deserialized from
/// <c>WorkflowStep.VideoAnalyzeConfigJson</c>. A closed, versioned, strongly-typed record —
/// the same house style as <see cref="ExtractStepConfig"/>: deterministic, non-LLM, and never
/// throws (every failure mode is representable in the resulting <c>{view, meta}</c> envelope
/// rather than as an exception).
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
    VideoAnalyzeExpectation? Expect = null);
