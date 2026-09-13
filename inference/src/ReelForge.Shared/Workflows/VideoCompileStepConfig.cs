namespace ReelForge.Shared.Workflows;

/// <summary>
/// How <c>VideoCompile</c> assembles the kept spans into the output file. <c>Reencode</c> (the
/// default) is frame-accurate via a select/aselect filtergraph; <c>StreamCopy</c> is fast and
/// lossless but can only cut on keyframes, so it requires an explicit opt-in
/// (<see cref="VideoCompileStepConfig.AllowKeyframeSnapping"/>).
/// </summary>
public enum VideoCompileMode
{
    Reencode,
    StreamCopy
}

/// <summary>
/// Cheap, in-process structural checks run against the resolved edit before encoding starts.
/// No model call. <see cref="MinRetainedRatio"/> exists specifically to refuse an edit that
/// discards nearly everything the source video contained.
/// </summary>
public sealed record VideoCompileExpectation(
    double? MinOutputSeconds = null,
    double? MaxOutputSeconds = null,
    double? MinRetainedRatio = 0.15,
    double? MaxRetainedRatio = null);

/// <summary>
/// JSON configuration for a <c>StepType.VideoCompile</c> workflow step. Deserialized from
/// <c>WorkflowStep.VideoCompileConfigJson</c>. A closed, versioned, strongly-typed record — the
/// same house style as <see cref="ExtractStepConfig"/>.
/// </summary>
/// <remarks>
/// Reusing <see cref="ExtractInputRef"/> for <see cref="Decision"/> is deliberate: the frontend
/// already has a TS mirror and an input-ref picker for it, so it is reused verbatim rather than
/// invented a second time. Only <c>From = Previous</c> or <c>From = Step</c> are valid decision
/// sources here (validated at execution time, not by the type system) — an Extract step's own
/// <c>Accumulated</c>/<c>ProjectFiles</c> sources make no sense as an edit decision.
/// </remarks>
public sealed record VideoCompileStepConfig(
    int Version,
    ExtractInputRef Decision,
    int AnalysisStepOrder,               // which VideoAnalyze step's full artifact to resolve ids against
    Guid? AnalysisStepResultId = null,   // cross-execution override: resolve a prior run's artifact instead of this execution's
    VideoCompileMode Mode = VideoCompileMode.Reencode,
    int PrePaddingMs = 80,
    int PostPaddingMs = 120,
    int MinSegmentMs = 250,
    int MaxSegments = 200,
    bool AllowKeyframeSnapping = false,  // required=true whenever Mode=StreamCopy
    string OutputFileName = "edited.mp4",
    // Allowlisted at execution time — these are workflow-author-supplied config, not model
    // output, but they still reach ffmpeg argv, so they are validated just as strictly.
    string VideoCodec = "libx264",
    string AudioCodec = "aac",
    int Crf = 20,
    string Preset = "veryfast",
    bool RegisterProjectFile = true,     // registers the compiled video as a re-editable ProjectFile row
    VideoCompileExpectation? Expect = null);
