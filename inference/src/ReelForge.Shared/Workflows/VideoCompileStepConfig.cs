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
/// How the background-music bed (see docs/video-editing.md "Background music") behaves relative
/// to dialogue. <c>SpeechEnvelope</c> (the default) lifts the music during non-speech windows via
/// a deterministic keyframed <c>volume</c> envelope computed from the analysis artifact's own
/// silence gaps/transcript segments — never a runtime audio-level compressor
/// (<c>sidechaincompress</c>), which this codebase deliberately does not use here since its
/// behavior depends on the actual waveform and cannot be asserted to an exact filter string or
/// explained in the step's own output. <c>Off</c> is a constant ducked bed throughout — the
/// simplest, always-available fallback.
/// </summary>
public enum MusicDuckingMode
{
    Off,
    SpeechEnvelope
}

/// <summary>
/// How a background-music track's own duration relates to the compiled edit's duration. See
/// docs/video-editing.md "Background music".
/// </summary>
public enum MusicFit
{
    /// <summary>The track loops (ffmpeg <c>-stream_loop -1</c>) to fill the whole edit, then is trimmed to the edit's exact frame-quantized length.</summary>
    LoopToFit,

    /// <summary>The track plays once and is trimmed to its own length if shorter than the edit — no loop, no fill.</summary>
    PlayOnce
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
    // -- Phase 3: optional motion-graphics overlays (see docs/video-editing.md
    //    "Motion graphics (Phase 3)"). EnableGraphics=false (default) is byte-identical to the
    //    pre-Phase-3 compile path — this is the load-bearing backward-compatibility guarantee. --
    /// <summary>
    /// Which step's resolved <c>MotionGraphicsPlanOutput</c> to apply. <c>null</c> (default) means
    /// no graphics plan is even looked for. Reuses <see cref="ExtractInputRef"/> verbatim, same as
    /// <see cref="Decision"/> — only <c>From = Previous</c> or <c>From = Step</c> are valid.
    /// </summary>
    ExtractInputRef? GraphicsPlan = null,
    bool EnableGraphics = false,
    int MaxOverlays = 20,
    int OverlayShortMs = 1500,
    int OverlayMediumMs = 3000,
    int OverlayHoldMs = 6000,
    int OverlayFadeMs = 300,
    /// <summary>Percent of frame height. Clamped 2..12 at execution time.</summary>
    int OverlayFontSizePct = 5,
    /// <summary>
    /// Percent of FRAME height the drawn overlay box (drawbox/drawtext background, and the box a
    /// rendered-asset overlay is stretch-scaled into) actually occupies — never the raw named
    /// safe-zone band's own height, which is a placement SAFE ZONE (LowerThird/UpperThird are the
    /// frame's outer third), not a target size. Clamped 6..40 at execution time, and never allowed
    /// to exceed the band's own height either way, so the box always stays inside the safe zone it
    /// was offered. Default 16 (a compact accent strip, not a takeover) — see
    /// DrawtextFilterBuilder.ComputeAccentBoxPixels and docs/video-editing.md "Motion graphics
    /// (Phase 3)".
    /// </summary>
    int OverlayBoxHeightPct = 16,
    /// <summary>
    /// Percent of the named band's own WIDTH the drawn overlay box occupies, centered — so the box
    /// is never full-bleed edge-to-edge. Clamped 30..100 at execution time. Default 82.
    /// </summary>
    int OverlayBoxWidthPct = 82,
    // Allowlisted at execution time exactly like VideoCodec/AudioCodec/Preset above — these are
    // workflow-author-supplied config, but still reach ffmpeg's drawtext/drawbox filter string.
    string OverlayFontColor = "white",
    string OverlayBoxColor = "black@0.45",
    int MaxOverlayTextChars = 80,
    int MaxOverlaySubtextChars = 60,
    // -- Background music (see docs/video-editing.md "Background music"). EnableMusic=false
    //    (default) is byte-identical to the pre-music compile path — this is the load-bearing
    //    backward-compatibility guarantee of this whole addition, exactly like EnableGraphics. --
    /// <summary>
    /// Which step's resolved <c>MusicPlanOutput</c> to apply. <c>null</c> (default) means no plan
    /// is even looked for — the deterministic <see cref="MusicTrackProjectFileId"/> path (or no
    /// music) is used instead. Reuses <see cref="ExtractInputRef"/> verbatim, same as
    /// <see cref="Decision"/>/<see cref="GraphicsPlan"/> — only <c>From = Previous</c> or
    /// <c>From = Step</c> are valid.
    /// </summary>
    ExtractInputRef? MusicPlan = null,
    /// <summary>
    /// A specific <c>audio/*</c> project file to use as the background-music track — the
    /// deterministic path (no agent required), and also the fallback when <see cref="MusicPlan"/>
    /// is unresolvable/invalid or names an unoffered track id.
    /// </summary>
    Guid? MusicTrackProjectFileId = null,
    /// <summary>Applies the resolved music track during the same encode. <c>false</c> (default) is byte-identical to the pre-music compile path. Requires <c>Mode = Reencode</c> and <c>AudioCodec != "copy"</c>.</summary>
    bool EnableMusic = false,
    MusicDuckingMode MusicDucking = MusicDuckingMode.SpeechEnvelope,
    MusicFit MusicFitPolicy = MusicFit.LoopToFit,
    int MusicFadeInMs = 1500,
    int MusicFadeOutMs = 2500,
    // Bed level (dBFS) keyed by the model's Intensity word — exactly how OverlayShortMs/
    // MediumMs/HoldMs key off Duration. Clamped to [-40, -6] at execution time.
    int MusicBedQuietDb = -26,
    int MusicBedBalancedDb = -20,
    int MusicBedFeatureDb = -14,
    // Attenuation (dB, <= 0) applied BELOW the bed while dialogue is present, keyed by the
    // model's Ducking word. Clamped to [-30, 0] at execution time.
    int MusicDuckLightDb = -6,
    int MusicDuckNormalDb = -11,
    int MusicDuckHeavyDb = -18,
    /// <summary>Linear gain ramp (ms) INSIDE each lift window — the music is never above the ducked level exactly at a window boundary, so a lift can never bleed into adjacent dialogue.</summary>
    int MusicDuckRampMs = 400,
    /// <summary>Non-speech windows shorter than this (and shorter than twice the ramp) are never lifted at all — a lift too short to fully ramp reads as pumping, not a genuine break.</summary>
    int MinMusicLiftWindowMs = 1200,
    /// <summary>Caps the volume-envelope expression's length; excess windows are dropped, longest first, then re-sorted chronologically.</summary>
    int MaxMusicLiftWindows = 12,
    /// <summary>Lift windows closer together than this are merged into one.</summary>
    int MusicLiftMergeMs = 400,
    VideoCompileExpectation? Expect = null);
