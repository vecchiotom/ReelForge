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
///
/// <para>
/// Phase 1 ("scene/visual analysis") bumps <see cref="Version"/> to 2 and appends
/// <see cref="DuplicateGroups"/> and <see cref="Pacing"/> at the end of this record, and
/// <see cref="VideoAnalysisShot"/> gains optional <c>Visual</c>/<c>Audio</c> descriptors. Every
/// Phase 1 addition is optional/nullable/default-valued and strictly appended — an artifact
/// persisted with <c>Version: 1</c> (no visual/audio descriptors at all) still deserializes into
/// this same record with those new fields simply absent/null.
/// </para>
/// <para>
/// Phase 2 ("vision shot captioning") stays at <see cref="Version"/> 2 rather than bumping to 3:
/// it appends exactly one more optional/nullable field (<see cref="VideoAnalysisShot.Caption"/>)
/// plus optional/default-valued fields on <see cref="VideoAnalysisProvenance"/>, and a
/// Version-2-without-captions artifact and a Version-2-with-captions artifact are both valid
/// under the identical shape — no consumer needs to structurally distinguish them (a consumer
/// that cares simply checks whether <c>Caption</c> is null). A version bump is reserved for a
/// change that breaks or reshapes existing fields, not for another purely-additive optional one.
/// </para>
/// </remarks>
public sealed record VideoAnalysisArtifact(
    int Version,
    VideoAnalysisMedia Media,
    IReadOnlyList<VideoAnalysisShot> Shots,
    IReadOnlyList<VideoAnalysisSilenceSpan> SilenceSpans,
    IReadOnlyList<VideoAnalysisSegment> Segments,
    IReadOnlyList<VideoAnalysisWord> Words,
    IReadOnlyList<string> OfferedIds,
    VideoAnalysisProvenance Provenance,
    IReadOnlyList<VideoAnalysisDuplicateGroup>? DuplicateGroups = null,
    VideoAnalysisPacing? Pacing = null,
    /// <summary>
    /// Phase 3 addition — deterministic overlay-placement candidates (see
    /// docs/video-editing.md "Motion graphics (Phase 3)"), populated only when
    /// <c>VideoAnalyzeStepConfig.EmitOverlayPlacements</c> is set. A SEPARATE id namespace
    /// (<c>p{n}</c>) from shots/silences/segments — deliberately NOT resolvable by
    /// <c>VideoCompileStepExecutor.BuildIdTimeIndex</c>, which resolves only cut-anchor ids.
    /// </summary>
    IReadOnlyList<VideoAnalysisPlacement>? Placements = null,
    /// <summary>
    /// Exactly which placement ids were actually included in the bounded view shown to the
    /// motion-graphics agent — the Phase 3 analogue of <see cref="OfferedIds"/>, but a SEPARATE
    /// list/budget: a placement id must never be validated against <see cref="OfferedIds"/>, and
    /// a cut-anchor id must never be validated against this list.
    /// </summary>
    IReadOnlyList<string>? OfferedPlacementIds = null,
    /// <summary>
    /// Multi-source addition. One entry per analyzed source clip, in source-index order,
    /// recording exactly what <see cref="VideoAnalyzeStepExecutor"/> downloaded/probed for that
    /// clip — the same storage key <c>VideoCompileStepExecutor</c> must download to physically cut
    /// from it, and the same per-clip <see cref="VideoAnalysisMedia"/> (duration/fps/dimensions)
    /// every clip-aware computation (frame quantization, padding clamps, graphics geometry) must
    /// use instead of the single top-level <see cref="Media"/>. Null for an artifact produced
    /// before this field existed (a true legacy artifact, not merely a single-source one) — those
    /// deserialize with every item's <see cref="VideoAnalysisShot.SourceIndex"/> etc. defaulting to
    /// 0 and are the ONE case <c>VideoCompileStepExecutor</c> still re-derives the source storage
    /// key the old way (walking the VideoAnalyze step's own config) rather than reading it directly
    /// from here. Every artifact produced by the current executor populates this with at least one
    /// entry, even for a single source, so the top-level <see cref="Media"/> and
    /// <c>Sources[0].Media</c> agree exactly for that case.
    /// </summary>
    IReadOnlyList<VideoAnalysisSourceInfo>? Sources = null,
    /// <summary>
    /// Background-music addition (see docs/video-editing.md "Background music") — populated only
    /// when <c>VideoAnalyzeStepConfig.OfferMusicTracks</c> is set. One entry per <c>audio/*</c>
    /// project file, project-level (not per-source, unlike everything above). A SEPARATE id
    /// namespace (<c>m{n}</c>) from shots/silences/segments/placements — deliberately NOT
    /// resolvable by <c>VideoCompileStepExecutor.BuildIdTimeIndex</c>.
    /// </summary>
    IReadOnlyList<VideoAnalysisMusicCandidate>? MusicCandidates = null,
    /// <summary>
    /// Exactly which music-track ids were actually included in the bounded view shown to the
    /// music-supervisor agent — the background-music analogue of <see cref="OfferedIds"/>/
    /// <see cref="OfferedPlacementIds"/>, but its OWN separate list: a music-track id must never
    /// be validated against either of those, and vice versa.
    /// </summary>
    IReadOnlyList<string>? OfferedMusicIds = null,
    /// <summary>
    /// Phase 4 addition — cross-source shot "look" (light/grade) groups computed by
    /// <c>FrameGridAnalyzer.GroupLooks</c>, populated only when
    /// <c>VideoAnalyzeStepConfig.DetectLookGroups</c> is set. A SEPARATE id namespace (<c>k{n}</c>)
    /// from every other id family — purely DESCRIPTIVE, deliberately never resolvable by
    /// <c>VideoCompileStepExecutor.BuildIdTimeIndex</c> (see docs/video-editing.md "Semantic visual
    /// dimensions (Phase 4)").
    /// </summary>
    IReadOnlyList<VideoAnalysisLookGroup>? LookGroups = null,
    /// <summary>
    /// Tracked screen-insert addition (see docs/video-editing.md "Tracked screen inserts
    /// (Phase 5)") — deterministic chroma-plate quad tracks computed by <c>ChromaQuadTracker</c>,
    /// populated only when <c>VideoAnalyzeStepConfig.DetectInsertRegions</c> is set. A SEPARATE id
    /// namespace (<c>r{n}</c>) from every other id family — deliberately NOT resolvable by
    /// <c>VideoCompileStepExecutor.BuildIdTimeIndex</c>. The full per-frame numeric tracking data
    /// (<see cref="VideoInsertRegionTrack.Keyframes"/>) lives ONLY here, never in the bounded
    /// view — the agent sees an opaque id plus qualitative descriptors, and
    /// <c>VideoCompileStepExecutor</c> alone resolves the id back to these numbers.
    /// </summary>
    IReadOnlyList<VideoInsertRegionTrack>? InsertRegions = null,
    /// <summary>
    /// Exactly which insert-region ids were actually included in the bounded view shown to the
    /// motion-graphics agent — the screen-insert analogue of <see cref="OfferedIds"/>/
    /// <see cref="OfferedPlacementIds"/>/<see cref="OfferedMusicIds"/>, but its OWN separate list:
    /// an insert-region id must never be validated against any of those, and vice versa.
    /// </summary>
    IReadOnlyList<string>? OfferedInsertRegionIds = null,
    /// <summary>
    /// Sound-effects addition (see docs/video-editing.md "Sound effects") — populated only when
    /// <c>VideoAnalyzeStepConfig.OfferSfxClips</c> is set. One entry per <c>audio/*</c> project
    /// file, project-level (not per-source), exactly like <see cref="MusicCandidates"/>. A
    /// SEPARATE id namespace (<c>x{n}</c>) from every other id family — deliberately NOT
    /// resolvable by <c>VideoCompileStepExecutor.BuildIdTimeIndex</c>: an SFX-clip id names a
    /// FILE, never a moment; the MOMENT a cue fires at is a separate, offered cut-anchor id.
    /// </summary>
    IReadOnlyList<VideoAnalysisSfxCandidate>? SfxCandidates = null,
    /// <summary>
    /// Exactly which SFX-clip ids were actually included in the bounded view shown to the
    /// sound-designer agent — the sound-effects analogue of <see cref="OfferedMusicIds"/>, but
    /// its OWN separate list: an SFX-clip id must never be validated against any other offered-id
    /// list, and vice versa.
    /// </summary>
    IReadOnlyList<string>? OfferedSfxIds = null);

/// <summary>
/// One tracked frame (or keyframe) of a chroma-plate insert region's deforming quadrilateral —
/// four corners in NORMALIZED frame coordinates (0..1 of frame width/height, resolution-
/// independent), in ffmpeg <c>perspective</c>-filter corner order: 0 = top-left, 1 = top-right,
/// 2 = bottom-left, 3 = bottom-right. <see cref="TimeSec"/> is on the owning SOURCE clip's own
/// timeline. Produced only by deterministic C# (<c>ChromaQuadTracker</c>) — no model ever
/// authors, edits, or even sees one of these.
/// </summary>
public sealed record VideoInsertQuadKeyframe(
    double TimeSec,
    double X0, double Y0,
    double X1, double Y1,
    double X2, double Y2,
    double X3, double Y3);

/// <summary>
/// One tracked chroma-plate insert region — a contiguous run of frames in which
/// <c>ChromaQuadTracker</c> found a uniform-color quadrilateral plate (e.g. a green screen inside
/// a phone held in frame) suitable for compositing a rendered scene into. Id: <c>r{n}</c> — a
/// SEPARATE namespace from every cut-anchor/placement/music/look id, never resolvable by
/// <c>VideoCompileStepExecutor.BuildIdTimeIndex</c>. The bounded view offers only the id plus
/// qualitative descriptors (confidence/size/motion buckets); the per-frame numeric
/// <see cref="Keyframes"/> are compile-time-only data.
/// </summary>
public sealed record VideoInsertRegionTrack(
    string Id,
    /// <summary>The shot the track's midpoint falls in — view legibility only, no execution meaning (mirrors <see cref="VideoAnalysisSilenceSpan.AfterShot"/>).</summary>
    string? ShotId,
    double StartSec,
    double EndSec,
    IReadOnlyList<VideoInsertQuadKeyframe> Keyframes,
    /// <summary>0..1 — detection coverage x mean quad fill ratio; <c>VideoCompileStepConfig.MinInsertConfidence</c> gates on this.</summary>
    double Confidence,
    /// <summary>Mean fraction of the frame's area the tracked quad covers (0..1).</summary>
    double MeanAreaRatio,
    /// <summary>Mean width/height ratio of the tracked quad in pixel space — the aspect hint the agent uses to render suitably-shaped content.</summary>
    double MeanAspectRatio,
    /// <summary>"Static" / "Slow" / "Moving" — mean centroid displacement bucket.</summary>
    string MotionClass,
    /// <summary>The chroma plate color that matched ("green"/"blue"/"magenta") — C#-side matching only, never an ffmpeg value.</summary>
    string ColorName,
    /// <summary>Multi-source addition — see <see cref="VideoAnalysisShot.SourceIndex"/>.</summary>
    int SourceIndex = 0);

/// <summary>
/// A group of shots that share a similar light/grade "look" (see <c>FrameGridAnalyzer.GroupLooks</c>),
/// single-linkage clustered over the whole artifact — deliberately NOT windowed like
/// <see cref="VideoAnalysisDuplicateGroup"/>, since a shared look is routinely NOT temporally
/// adjacent (e.g. interior coverage at the start of one clip and the end of another). Id: <c>k{n}</c>.
/// <see cref="ShotIds"/> is in first-member (ascending shot-index) order; <see cref="RepresentativeShotId"/>
/// is the member with <c>LookRank == 0</c> (closest to the group centroid).
/// </summary>
public sealed record VideoAnalysisLookGroup(
    string Id,
    IReadOnlyList<string> ShotIds,
    string RepresentativeShotId,
    double Cohesion,
    string? ColorTemperatureClass = null,
    string? ToneClass = null,
    string? SaturationClass = null);

/// <summary>
/// One candidate background-music track — an <c>audio/*</c> project file offered to
/// <c>AgentType.MusicSupervisor</c>. Id: <c>m{n}</c> — a SEPARATE namespace from
/// <c>s{n}</c>/<c>g{n}</c>/<c>t{n}</c>/<c>p{n}</c>, never resolvable by
/// <c>VideoCompileStepExecutor.BuildIdTimeIndex</c>. Deliberately small: no storage key, no size —
/// <c>VideoCompileStepExecutor</c> resolves <see cref="ProjectFileId"/> back to a storage key
/// itself once a track is actually chosen, so the model never sees or needs one.
/// </summary>
public sealed record VideoAnalysisMusicCandidate(
    string Id,
    Guid ProjectFileId,
    string FileName,
    string MimeType,
    long SizeBytes);

/// <summary>
/// One candidate sound-effect clip — an <c>audio/*</c> project file offered to
/// <c>AgentType.SoundDesigner</c>. Id: <c>x{n}</c> — a SEPARATE namespace from
/// <c>s{n}</c>/<c>g{n}</c>/<c>t{n}</c>/<c>p{n}</c>/<c>m{n}</c>, never resolvable by
/// <c>VideoCompileStepExecutor.BuildIdTimeIndex</c>. Deliberately identical in shape to
/// <see cref="VideoAnalysisMusicCandidate"/> and just as small: no storage key, no duration —
/// <c>VideoCompileStepExecutor</c> resolves <see cref="ProjectFileId"/> back to a storage key
/// itself once a cue actually names this clip, so the model never sees or needs one.
/// </summary>
public sealed record VideoAnalysisSfxCandidate(
    string Id,
    Guid ProjectFileId,
    string FileName,
    string MimeType,
    long SizeBytes);

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

/// <summary>
/// Multi-source addition — records one analyzed source clip: which physical object it came from
/// and what was probed about it. See <see cref="VideoAnalysisArtifact.Sources"/>.
/// </summary>
public sealed record VideoAnalysisSourceInfo(
    int SourceIndex,
    string StorageKey,
    VideoAnalysisMedia Media);

/// <summary>
/// A detected shot/scene. Id: <c>s{n}</c>, assigned by index in scene-detection order.
/// <see cref="Visual"/>/<see cref="Audio"/> are Phase 1 additions — deterministic, ffmpeg+pure-C#
/// derived descriptors, populated only when <c>VideoAnalyzeStepConfig.AnalyzeVisuals</c>/
/// <c>AnalyzeAudioLevels</c> are on AND analysis did not degrade (see
/// <see cref="VideoAnalysisProvenance.VisualAnalysisDegraded"/>).
/// </summary>
public sealed record VideoAnalysisShot(
    string Id,
    double StartSec,
    double EndSec,
    VideoAnalysisShotVisual? Visual = null,
    VideoAnalysisShotAudio? Audio = null,
    /// <summary>
    /// Phase 2 addition — a short, AI-generated scene description, populated only when this shot
    /// was selected for captioning by <c>KeyframeSelector.SelectShotsToCaption</c> AND captioning
    /// did not fail/degrade for it. Absence is normal (captioning is budget-limited and opt-in via
    /// <c>VideoAnalyzeStepConfig.Vision</c>), never a signal the shot is empty or unimportant.
    /// </summary>
    VideoShotCaption? Caption = null,
    /// <summary>
    /// Multi-source addition — which entry of <see cref="VideoAnalysisArtifact.Sources"/> this
    /// shot was detected in. <see cref="StartSec"/>/<see cref="EndSec"/> are always relative to
    /// THIS source's own timeline, never a global one — there is no such thing as a shared
    /// timeline across distinct source clips. Defaults to 0, so a legacy (pre-multi-source)
    /// artifact — which only ever had one implicit source — deserializes as if every item
    /// explicitly said "source 0", the only source that could exist.
    /// </summary>
    int SourceIndex = 0);

/// <summary>
/// A detected silence gap. Id: <c>g{n}</c>. <see cref="AfterShot"/> links each gap to the shot
/// it trails, purely to make the bounded prompt view legible — it carries no execution meaning.
/// </summary>
public sealed record VideoAnalysisSilenceSpan(
    string Id,
    double StartSec,
    double EndSec,
    string? AfterShot = null,
    /// <summary>Multi-source addition — see <see cref="VideoAnalysisShot.SourceIndex"/>.</summary>
    int SourceIndex = 0);

/// <summary>
/// A transcript segment. Id: <c>t{n}</c>. <see cref="Shot"/> links each segment back to the shot
/// it falls within, again only to aid the prompt view.
/// </summary>
public sealed record VideoAnalysisSegment(
    string Id,
    string? Shot,
    double StartSec,
    double EndSec,
    string Text,
    /// <summary>Multi-source addition — see <see cref="VideoAnalysisShot.SourceIndex"/>.</summary>
    int SourceIndex = 0);

/// <summary>
/// A single transcribed word. Id: <c>w{n}</c>. Persisted in the full artifact for audit and as
/// the phase-2 basis for subtitle export, but never offered to the model directly in v1 — the
/// segment is the offered granularity (see plan §8, "Explicitly NOT in this iteration").
/// </summary>
public sealed record VideoAnalysisWord(
    string Id,
    double StartSec,
    double EndSec,
    string Text,
    /// <summary>Multi-source addition — see <see cref="VideoAnalysisShot.SourceIndex"/>.</summary>
    int SourceIndex = 0);

/// <summary>
/// Records how this artifact was produced, for audit and as the basis for the
/// <c>meta.transcription</c>/<c>meta.visual</c>/<c>meta.audioLevels</c> blocks surfaced in the
/// bounded prompt view (including the "degraded" signal when a best-effort analysis stage fell
/// back to producing no data rather than failing the whole step).
/// </summary>
public sealed record VideoAnalysisProvenance(
    VideoTranscriptionMode TranscriptionMode,
    bool TranscriptionApplied,
    bool TranscriptionDegraded,
    string? TranscriptionProvider = null,
    string? TranscriptionLanguage = null,
    DateTime? AnalyzedAt = null,
    bool VisualAnalysisApplied = false,
    bool VisualAnalysisDegraded = false,
    bool AudioLevelsApplied = false,
    bool SharpnessAvailable = false,
    // -- Phase 2: vision shot captioning (see docs/video-editing.md "Vision captioning") --
    VideoVisionMode VisionMode = VideoVisionMode.Off,
    bool VisionApplied = false,
    bool VisionDegraded = false,
    string? VisionProvider = null,
    int CaptionedShotCount = 0,
    /// <summary>
    /// True when the aggregate <c>VideoAnalyzeStepConfig.VisionTimeoutSeconds</c> wall-clock
    /// budget was exceeded mid-pass, so captioning stopped early with whatever succeeded so far
    /// (as opposed to <see cref="VisionDegraded"/>, which covers "no vision provider resolved" or
    /// "zero captions obtained at all").
    /// </summary>
    bool VisionPartial = false,
    // -- Phase 4: semantic visual dimensions (see docs/video-editing.md "Semantic visual
    //    dimensions (Phase 4)") --
    /// <summary>D1-D3 (and the look pass below reading them) actually ran and produced data.</summary>
    bool ColorGradingApplied = false,
    /// <summary>D4 look grouping (<c>artifact.LookGroups</c>) actually ran.</summary>
    bool LookGroupingApplied = false,
    /// <summary>Count of <c>artifact.LookGroups</c> — carried on provenance too so <c>meta.look</c> never has to dereference the (possibly-trimmed) view list.</summary>
    int LookGroupCount = 0,
    /// <summary>True when exactly one look group covers every analyzed shot — the single-camera-talking-head case, where per-shot <c>look</c> keys carry no discriminating information and are suppressed (see docs/video-editing.md).</summary>
    bool LookUniform = false,
    /// <summary>D6 letterbox/pillarbox detection actually ran (<c>VideoAnalyzeStepConfig.DetectLetterbox</c> was on AND visual analysis applied).</summary>
    bool LetterboxDetectionApplied = false,
    /// <summary>Count of shots that received a real (non-null) <c>Sharpness</c> measurement this step.</summary>
    int SharpnessShotCount = 0,
    // -- Tracked screen inserts (see docs/video-editing.md "Tracked screen inserts (Phase 5)") --
    /// <summary>The chroma-plate insert-region tracking pass (<c>ChromaQuadTracker</c>) actually ran and produced data (possibly zero tracks).</summary>
    bool InsertTrackingApplied = false,
    /// <summary>Insert-region tracking was configured on but failed and degraded to "no tracks" rather than failing the step — same convention as <see cref="VisualAnalysisDegraded"/>.</summary>
    bool InsertTrackingDegraded = false,
    /// <summary>Count of <c>artifact.InsertRegions</c> tracks found across every source.</summary>
    int InsertRegionCount = 0);

// =============================================================================================
// Phase 1: visual/audio scene descriptors (grid-sampling based, deterministic, no LLM call).
// See docs/video-editing.md "Scene/visual analysis (Phase 1)" for the full design.
// =============================================================================================

/// <summary>
/// A calm/motionless window within a shot: a run of consecutive sampled grid frames whose
/// per-frame motion stayed below <c>VideoAnalyzeStepConfig.StillMotionThreshold</c> for at least
/// <c>MinStillWindowMs</c>. Useful to a downstream cutter/story-editor for "land the cut here,
/// not mid-motion".
/// </summary>
public sealed record VideoAnalysisStillWindow(double StartSec, double EndSec, double MeanMotion);

/// <summary>One of a shot's top-3 dominant colors, from a 64-bin (4 levels/channel) RGB histogram.</summary>
public sealed record VideoAnalysisColor(string Hex, double Share);

/// <summary>A normalized (0..1 of frame width/height) rectangle — resolution-independent.</summary>
public sealed record VideoAnalysisRect(double X, double Y, double W, double H);

/// <summary>
/// One spatial region of a shot (either a 3x3 grid cell, named <c>R0</c>..<c>R8</c> row-major, or
/// one of the three named overlay-candidate bands <c>LowerThird</c>/<c>UpperThird</c>/
/// <c>CenterBand</c>) with descriptors relevant to placing a text/graphic overlay there in a
/// later phase. See <c>FrameGridAnalyzer</c> for the exact <see cref="Suitability"/> formula.
/// </summary>
public sealed record VideoAnalysisRegion(
    string Name,
    VideoAnalysisRect Rect,
    double LumaMean,
    double LumaStdDev,
    double TemporalMotion,
    double Suitability,
    string TextColor);

/// <summary>
/// Deterministic, ffmpeg+pure-C#-derived visual descriptors for one shot, computed by
/// <c>FrameGridAnalyzer</c> from a single low-res raw-frame grid pass over the source video (see
/// docs/video-editing.md). <see cref="CameraMove"/>/<see cref="CameraMoveConfidence"/> is a
/// documented heuristic (1-D SAD pixel-shift search), not ground truth — always read alongside
/// its confidence.
/// </summary>
public sealed record VideoAnalysisShotVisual(
    double MotionMean,
    double MotionPeak,
    double MotionStdDev,
    string MotionClass,
    string CameraMove,
    double CameraMoveConfidence,
    double HeadMotion,
    double TailMotion,
    IReadOnlyList<VideoAnalysisStillWindow> StillWindows,
    double BrightnessMean,
    double BrightnessStdDev,
    double ContrastRms,
    double ClippedHighlightRatio,
    double CrushedBlackRatio,
    double SaturationMean,
    IReadOnlyList<VideoAnalysisColor> DominantColors,
    IReadOnlyList<VideoAnalysisRegion> Regions,
    string? BestOverlayRegion,
    VideoAnalysisRect? ActiveCrop,
    double? Sharpness,
    bool KenBurnsCandidate,
    string? KenBurnsReason,
    string? DuplicateGroupId,
    int? GroupRank,
    bool IsBestTake,
    // -- Phase 4: semantic visual dimensions D1-D4/D6/D7 (see docs/video-editing.md "Semantic
    //    visual dimensions (Phase 4)"). All null/false/0 when AnalyzeColorGrading is off, or when
    //    visual analysis did not run at all — same "absent/null == not computed" convention as
    //    ActiveCrop/Sharpness above. --
    /// <summary>D1. "Warm" / "Cool" / "Neutral" — null when <c>AnalyzeColorGrading</c> is off.</summary>
    string? ColorTemperatureClass = null,
    /// <summary>D1. Normalized -1..1; a ±0.25 raw mean-channel difference saturates the scale.</summary>
    double Warmth = 0,
    /// <summary>D1. Normalized -1..1, green-magenta axis.</summary>
    double Tint = 0,
    /// <summary>D2. "Blown" / "Crushed" / "Flat" / "Contrasty" / "Normal" — null when <c>AnalyzeColorGrading</c> is off.</summary>
    string? ToneClass = null,
    /// <summary>D2. 5th-percentile luma, 0..1.</summary>
    double BlackPoint = 0,
    /// <summary>D2. 95th-percentile luma, 0..1.</summary>
    double WhitePoint = 0,
    /// <summary>D3. "Muted" / "Natural" / "Vivid", a pure projection of <see cref="SaturationMean"/> — null when <c>AnalyzeColorGrading</c> is off.</summary>
    string? SaturationClass = null,
    /// <summary>
    /// D7. Heuristic only — named with the codebase's existing "-Candidate" suffix for exactly this
    /// confidence level (see <see cref="KenBurnsCandidate"/>). Surfaced at Full detail only, and fed
    /// to the vision prompt so a model that can actually see the frame adjudicates it.
    /// </summary>
    bool BacklitCandidate = false,
    /// <summary>D4. This shot's look-group id (<c>k{n}</c>), or null when it belongs to no group (a look unlike any other shot analyzed).</summary>
    string? LookGroupId = null,
    /// <summary>D4. Rank within <see cref="LookGroupId"/> by ascending distance to the group centroid; 0 is the most representative member. Null when <see cref="LookGroupId"/> is null.</summary>
    int? LookRank = null);

/// <summary>
/// Deterministic audio-level descriptors for one shot, derived from <c>WavRmsSampler</c> windows
/// over the (already-extracted-for-transcription, or freshly-extracted) full-track WAV, plus the
/// already-detected silence spans for <see cref="SpeechRatio"/> — no new audio analysis pass.
/// </summary>
public sealed record VideoAnalysisShotAudio(
    double RmsDbfs,
    double PeakDbfs,
    double SpeechRatio,
    string LoudnessClass,
    // -- Phase 4 (D5): audio character — see docs/video-editing.md "Semantic visual dimensions
    //    (Phase 4)". A heuristic, hence paired with a confidence, exactly as CameraMove is paired
    //    with CameraMoveConfidence. All 0/default when AnalyzeAudioLevels is off. --
    /// <summary>Peak-to-RMS ratio in dB.</summary>
    double CrestFactorDb = 0,
    /// <summary>10th-percentile window RMS in dBFS — "how loud is this shot when nothing is happening".</summary>
    double NoiseFloorDbfs = 0,
    /// <summary>1 - clamp(stddev(window RMS)/12dB, 0, 1); 1 == perfectly level.</summary>
    double LevelStability = 0,
    /// <summary>Overlap-weighted zero-crossing rate of the shot's audio windows.</summary>
    double ZeroCrossingRate = 0,
    /// <summary>"Dialogue" / "Music" / "Ambient" / "Noisy" / "Silent" — null when <c>AnalyzeAudioLevels</c> is off.</summary>
    string? AudioCharacterClass = null,
    /// <summary>0..1 confidence in <see cref="AudioCharacterClass"/> — makes shipping a ZCR/crest/floor heuristic instead of an FFT classifier honest.</summary>
    double AudioCharacterConfidence = 0);

/// <summary>
/// A group of near-duplicate/multi-take shots (single-linkage clustered over a sliding time
/// window — see <c>FrameGridAnalyzer.GroupDuplicates</c>), so a downstream editor can prefer the
/// best take instead of keeping every attempt. <see cref="ShotIds"/> is in original chronological
/// (shot-index) order; <see cref="BestShotId"/> is the member with <c>GroupRank == 0</c>.
/// </summary>
public sealed record VideoAnalysisDuplicateGroup(
    string Id,
    IReadOnlyList<string> ShotIds,
    string BestShotId,
    double MeanSimilarity);

/// <summary>Whole-video pacing summary, computed once at the artifact level from the shot list.</summary>
public sealed record VideoAnalysisPacing(
    double MeanShotSeconds,
    double MedianShotSeconds,
    double CutsPerMinute,
    IReadOnlyList<double> MotionTimeline,
    double TimelineBinSeconds);

// =============================================================================================
// Phase 2: optional vision-LLM shot captioning (see docs/video-editing.md "Vision captioning").
// Purely additive on top of Phase 1's schema — Version stays 2 (see VideoAnalyzeStepExecutor's
// doc comment for why no Version 3 bump is needed).
// =============================================================================================

/// <summary>
/// A short, structured scene description of one shot's representative keyframe, produced by a
/// single vision-capable chat-completions call (<c>IShotCaptioner</c>). Every field is
/// model-authored prose/tags — deliberately no numeric or time-bearing property, same discipline
/// as <c>VideoEditDecisionOutput</c>, though for a different reason here: a caption describes one
/// still frame, so it has nothing meaningful to say about timing at all.
/// </summary>
/// <remarks>
/// <b>Safety property, enforced by the caller, not by this type:</b> <see cref="ShotId"/> as
/// returned by the model must never be trusted for binding a caption to a shot.
/// <c>VideoAnalyzeStepExecutor</c> always overwrites this field with the id it actually requested
/// before attaching the caption to a shot — see <c>IShotCaptioner.CaptionAsync</c>'s remarks.
/// </remarks>
public sealed record VideoShotCaption(
    string ShotId,
    string Summary,
    IReadOnlyList<string> Subjects,
    string Action,
    string Setting,
    string Mood,
    /// <summary>Free-text shot-scale guess, typically one of "Wide", "Medium", "CloseUp" — not enum-constrained since a vision model's own wording is more robust than forcing an exact enum match.</summary>
    string ShotScale,
    string CameraAngle,
    IReadOnlyList<string> OnScreenText,
    IReadOnlyList<string> Tags,
    /// <summary>"Day" | "Night" | "GoldenHour" | "Indoor" | "Unknown".</summary>
    string TimeOfDay,
    /// <summary>Interpretive prose, e.g. "soft window light from camera left", "harsh overhead".</summary>
    string Lighting,
    /// <summary>The grading/look read, e.g. "flat ungraded log", "warm documentary grade".</summary>
    string VisualStyle,
    /// <summary>Composition judgment, e.g. "centered, generous headroom", "subject cropped at frame edge".</summary>
    string Framing,
    /// <summary>
    /// The highest-value field in this record: the defects a human editor spots instantly that no
    /// pixel statistic reaches — "soft focus", "blown window", "visible banding", "rolling shutter".
    /// </summary>
    IReadOnlyList<string> TechnicalIssues);

// =============================================================================================
// Phase 3: optional motion-graphics overlay placement candidates (see docs/video-editing.md
// "Motion graphics (Phase 3)"). Purely additive on top of Phase 1/2's schema — Version stays 2,
// same rationale as Phase 2: every new field here is optional/nullable/default-valued.
// =============================================================================================

/// <summary>
/// One deterministic overlay-placement candidate, derived from a shot's Phase 1
/// <see cref="VideoAnalysisRegion"/> data by <c>OverlayPlacementBuilder</c>. Id: <c>p{n}</c> —
/// a SEPARATE namespace from <c>s{n}</c>/<c>g{n}</c>/<c>t{n}</c> cut-anchor ids, assigned by a
/// global counter across the whole artifact (not per-shot). Carries a fully server-resolved
/// time window ([<see cref="StartSec"/>, <see cref="EndSec"/>), clamped to the owning shot's own
/// bounds) so <c>VideoCompileStepExecutor</c> never has to guess a placement's timing — only its
/// on/off decision and text content are left to the motion-graphics agent.
/// </summary>
public sealed record VideoAnalysisPlacement(
    string Id,
    string ShotId,
    /// <summary>One of the three named overlay-candidate bands: "LowerThird" / "UpperThird" / "CenterBand".</summary>
    string Region,
    VideoAnalysisRect Rect,
    /// <summary>How <see cref="StartSec"/>/<see cref="EndSec"/> were chosen: "LongestStillWindow" / "ShotMiddle" / "ShotStart".</summary>
    string TimeAnchor,
    double StartSec,
    double EndSec,
    double Suitability,
    string TextColor,
    /// <summary>
    /// Multi-source addition — the source clip that owns this placement's <see cref="ShotId"/> (see
    /// <see cref="VideoAnalysisShot.SourceIndex"/>). Redundant with looking the owning shot up by
    /// id, but carried directly here so <c>VideoCompileStepExecutor</c> never has to do that lookup
    /// just to map a placement's source-timeline window onto the right source's output timeline.
    /// </summary>
    int SourceIndex = 0);
