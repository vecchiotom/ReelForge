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
/// Workflow-level gate for the deterministic per-seam transition system (see
/// docs/video-editing.md "Cut transitions"). <c>Off</c> (default) is byte-identical to the
/// pre-transition compile path — every seam is a hard butt-cut, exactly as before this addition.
/// <c>AudioOnly</c> forces every seam to a short audio-only ramp (no video crossfade at all).
/// <c>Auto</c>/<c>Expressive</c> select a per-seam <see cref="SeamTreatment"/> from a deterministic
/// rule table over measured shot facts (never a model) — <c>Expressive</c> picks visually bolder
/// treatments than <c>Auto</c> for the same measured facts (see <c>SeamTransitionPlanner</c>).
/// </summary>
public enum VideoTransitionPolicy { Off, AudioOnly, Auto, Expressive }

/// <summary>
/// The concrete treatment a single cut-seam receives, chosen deterministically by
/// <c>SeamTransitionPlanner</c> from measured facts — never configurable per-seam by a human, never
/// chosen by a model. <c>HardCut</c> is the pre-existing behavior (no fade of any kind).
/// <c>AudioOnly</c> keeps the video a hard cut but ramps audio through the seam. The remaining
/// values all apply a video crossfade/fade (requiring the ffmpeg <c>xfade</c>/<c>acrossfade</c>
/// filters — see <c>SeamTransitionPlanner</c>'s <c>xfadeAvailable</c> demotion path).
/// </summary>
public enum SeamTreatment { HardCut, AudioOnly, DipCut, SoftCut, Dissolve, DipToBlack, WhipBlur }

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
    VideoCompileExpectation? Expect = null,
    // -- Cut transitions (see docs/video-editing.md "Cut transitions"). Every field below defaults
    // to a byte-identical-to-before-this-addition value: ProgramFadeInMs/ProgramFadeOutMs/
    // ProgramAudioFadeInMs/ProgramAudioFadeOutMs all default to 0 (no program fade at all), and
    // TransitionPolicy defaults to Off (every seam a hard butt-cut, exactly as before). This is
    // the load-bearing backward-compatibility guarantee of this whole addition, exactly like
    // EnableGraphics/EnableMusic above. Appended AFTER Expect (not inserted earlier) so every
    // existing positional-construction call site and JSON payload keeps compiling/deserializing
    // unchanged. --
    /// <summary>Whole-piece video fade-IN duration (ms) at the very start of the compiled output. 0 (default) applies no fade.</summary>
    int ProgramFadeInMs = 0,
    /// <summary>Whole-piece video fade-OUT duration (ms) at the very end of the compiled output. 0 (default) applies no fade.</summary>
    int ProgramFadeOutMs = 0,
    /// <summary>Whole-piece audio fade-IN duration (ms). 0 (default) applies no fade. Requires the resolved audio codec to not be a stream-copy codec — see <c>PROGRAM_AUDIO_FADE_REQUIRES_AUDIO_REENCODE</c>.</summary>
    int ProgramAudioFadeInMs = 0,
    /// <summary>Whole-piece audio fade-OUT duration (ms). 0 (default) applies no fade. Same requirement as <see cref="ProgramAudioFadeInMs"/>.</summary>
    int ProgramAudioFadeOutMs = 0,
    /// <summary>Fade color for the program video fade-in/out (<c>fade=color=</c>). Allowlisted at execution time to <c>"black"</c>/<c>"white"</c> — see <c>AllowedProgramFadeColors</c>.</summary>
    string ProgramFadeColor = "black",
    /// <summary>Workflow-level gate for the per-seam transition system. <c>Off</c> (default) is byte-identical to the pre-transition compile path.</summary>
    VideoTransitionPolicy TransitionPolicy = VideoTransitionPolicy.Off,
    /// <summary>Half-width (ms) of the trapezoidal audio ramp <see cref="SeamTreatment.AudioOnly"/> applies around a seam.</summary>
    int AudioSeamRampMs = 24,
    /// <summary>Crossfade/fade-pair duration (ms) for <see cref="SeamTreatment.SoftCut"/>.</summary>
    int SoftCutMs = 200,
    /// <summary>Crossfade duration (ms) for <see cref="SeamTreatment.Dissolve"/>.</summary>
    int DissolveMs = 500,
    /// <summary>Crossfade duration (ms) for <see cref="SeamTreatment.DipToBlack"/> (an <c>xfade=fadeblack</c>).</summary>
    int DipToBlackMs = 600,
    /// <summary>Per-side fade duration (ms) for <see cref="SeamTreatment.DipCut"/> — a non-overlapping fade-out/fade-in pair, not a crossfade.</summary>
    int DipCutMs = 220,
    /// <summary>Hard cap (ms) on any single seam's crossfade overlap, regardless of the rule table's chosen duration.</summary>
    int MaxTransitionMs = 1200,
    /// <summary>Hard cap (percent of total seams) on how many seams may carry an OVERLAPPING treatment (OverlapSec &gt; 0) — excess seams downgrade to <see cref="SeamTreatment.AudioOnly"/>.</summary>
    int MaxTransitionRatioPct = 35,
    /// <summary>When the resolved cut list has more seams than this, the whole transition plan degrades to no overlaps at all (every seam <see cref="SeamTreatment.AudioOnly"/>/<see cref="SeamTreatment.HardCut"/>) rather than building an unbounded filtergraph.</summary>
    int MaxTransitionSegments = 80,
    /// <summary>A same-source removed gap at least this long (ms) is eligible to read as a deliberate section break (rule R4) rather than an ordinary trimmed pause.</summary>
    int SectionBreakGapMs = 8000,
    /// <summary>Reserved no-op flag for a future span-motion phase (speed ramps/Ken Burns at cuts) — deliberately unimplemented in this addition; always behaves as if <c>false</c>.</summary>
    bool EnableSpanMotion = false,
    // -- Tracked screen inserts (see docs/video-editing.md "Tracked screen inserts (Phase 5)").
    //    EnableInserts=false (default) is byte-identical to the pre-inserts compile path — the
    //    same load-bearing backward-compatibility guarantee as EnableGraphics/EnableMusic. --
    /// <summary>
    /// Composites the motion-graphics plan's chosen screen inserts (a Remotion-rendered scene
    /// corner-pinned into a tracked chroma-plate region via ffmpeg's per-frame-animated
    /// <c>perspective</c> filter) during the same encode. Inserts are read from the SAME resolved
    /// <c>MotionGraphicsPlanOutput</c> that <see cref="GraphicsPlan"/> references (the planner
    /// emits overlays and inserts in one output), so <see cref="GraphicsPlan"/> must be configured
    /// for inserts to resolve — but <see cref="EnableGraphics"/> itself need not be on. Requires
    /// <c>Mode = Reencode</c>. v1 limitation: skipped (soft, reported) on a multi-source compile or
    /// one with overlapping seam transitions — see docs/video-editing.md.
    /// </summary>
    bool EnableInserts = false,
    /// <summary>Tracks whose <c>VideoInsertRegionTrack.Confidence</c> falls below this are dropped (<c>confidence_below_threshold</c>) rather than composited badly.</summary>
    double MinInsertConfidence = 0.5,
    /// <summary>Cap on applied screen inserts; excess dropped in plan order (<c>max_inserts_exceeded</c>).</summary>
    int MaxInserts = 3,
    /// <summary>
    /// Cap on the per-insert count of corner keyframes baked into the ffmpeg <c>perspective</c>
    /// expressions (the full track is uniformly downsampled to at most this many). Bounds
    /// filtergraph-expression size; the filtergraph is written to a script file when long, so this
    /// is a per-frame-evaluation-cost knob, not a correctness one. Clamped 2..500.
    /// </summary>
    int MaxInsertExprKeyframes = 96,
    /// <summary>
    /// Fractional outward expansion of the tracked quad (about its centroid) before compositing,
    /// hiding the plate's own edge fringe under the inserted content. Clamped 0..0.1. Default 0.02.
    /// </summary>
    double InsertOverscan = 0.02,
    // -- Color grading (see docs/video-editing.md "Color grading"). EnableColorGrade=false
    //    (default) is byte-identical to the pre-grade compile path — the same load-bearing
    //    backward-compatibility guarantee as EnableGraphics/EnableMusic/EnableInserts. Appended
    //    AFTER InsertOverscan so every existing positional-construction call site and JSON
    //    payload keeps compiling/deserializing unchanged. --
    /// <summary>
    /// Which step's resolved <c>ColorGradePlanOutput</c> to apply. <c>null</c> (default) means no
    /// grade plan is even looked for. Reuses <see cref="ExtractInputRef"/> verbatim, same as
    /// <see cref="Decision"/>/<see cref="GraphicsPlan"/>/<see cref="MusicPlan"/> — only
    /// <c>From = Previous</c> or <c>From = Step</c> are valid. Points at either a solo
    /// <c>AgentType.Colorist</c> Agent step or a <c>StepType.ColorGradeRoom</c> step — both emit
    /// the exact same <c>ColorGradePlanOutput</c> shape.
    /// </summary>
    ExtractInputRef? ColorGradePlan = null,
    /// <summary>
    /// Applies the resolved colour grade (a whole-program ffmpeg
    /// <c>eq</c>/<c>colorbalance</c>/<c>colorlevels</c>/<c>hue</c> chain built entirely from
    /// first-party tables keyed by the plan's enum words — see <c>ColorGradeFilterBuilder</c>)
    /// during the same encode, BEFORE overlays/inserts are painted so graphics stay clean.
    /// <c>false</c> (default) is byte-identical to the pre-grade compile path. Requires
    /// <c>Mode = Reencode</c>. Every plan-level failure (missing/invalid plan, unknown look word,
    /// unavailable filters) degrades to "no grade applied", never to a failed compile — a plan
    /// whose <c>Look</c> is <c>"None"</c> is a valid decision to apply no grade.
    /// </summary>
    bool EnableColorGrade = false,
    // -- Sound effects (see docs/video-editing.md "Sound effects"). EnableSfx=false (default) is
    //    byte-identical to the pre-SFX compile path — the same load-bearing
    //    backward-compatibility guarantee as EnableGraphics/EnableMusic/EnableInserts/
    //    EnableColorGrade. Appended AFTER EnableColorGrade so every existing
    //    positional-construction call site and JSON payload keeps compiling/deserializing
    //    unchanged. --
    /// <summary>
    /// Which step's resolved <c>SfxPlanOutput</c> to apply. <c>null</c> (default) means no SFX
    /// plan is even looked for. Reuses <see cref="ExtractInputRef"/> verbatim, same as
    /// <see cref="Decision"/>/<see cref="GraphicsPlan"/>/<see cref="MusicPlan"/>/
    /// <see cref="ColorGradePlan"/> — only <c>From = Previous</c> or <c>From = Step</c> are
    /// valid. Unlike music's <see cref="MusicTrackProjectFileId"/>, there is deliberately NO
    /// deterministic no-agent fallback field: cue placement (WHICH clip at WHICH moment) is
    /// inherently editorial, and a blanket "same stinger at every cut" config path would be a
    /// footgun, not a capability — see docs/video-editing.md "Sound effects".
    /// </summary>
    ExtractInputRef? SfxPlan = null,
    /// <summary>
    /// Mixes the resolved SFX cues into the output audio during the same encode. <c>false</c>
    /// (default) is byte-identical to the pre-SFX compile path. Requires <c>Mode = Reencode</c>
    /// and <c>AudioCodec != "copy"</c> (the same pair of hard config errors music enforces).
    /// Every plan/cue-level failure degrades to "no SFX applied" / "drop this one cue", never a
    /// failed compile.
    /// </summary>
    bool EnableSfx = false,
    /// <summary>Cap on applied SFX cues; excess dropped in plan order (<c>max_cues_exceeded</c>).</summary>
    int MaxSfxCues = 8,
    /// <summary>
    /// Hard cap (seconds) on any single cue's play window — a long file misused as a cue is
    /// trimmed rather than running under the whole edit (a bed belongs to <see cref="EnableMusic"/>,
    /// not here). Clamped to at least 0.25s at execution time.
    /// </summary>
    double MaxSfxCueSeconds = 4.0,
    // Cue gain (dBFS attenuation relative to full scale) keyed by the model's Volume word —
    // exactly how MusicBedQuietDb/BalancedDb/FeatureDb key off Intensity. Clamped [-40, 0] at
    // execution time.
    int SfxSubtleDb = -18,
    int SfxNormalDb = -12,
    int SfxStrongDb = -6,
    /// <summary>How far (ms) a <c>Timing: "Lead"</c> cue fires BEFORE its anchor's output-timeline start moment.</summary>
    int SfxLeadMs = 150,
    /// <summary>How far (ms) a <c>Timing: "Lag"</c> cue fires AFTER its anchor's output-timeline start moment.</summary>
    int SfxLagMs = 150,
    /// <summary>Declick fade-out (ms) at the end of each cue's play window, so a MaxSfxCueSeconds-trimmed clip never ends on a hard edge. Capped at half the cue's own play window.</summary>
    int SfxFadeOutMs = 120);
