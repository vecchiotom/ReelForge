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
    VideoAnalysisPacing? Pacing = null);

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
    VideoAnalysisShotAudio? Audio = null);

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
    bool SharpnessAvailable = false);

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
    bool IsBestTake);

/// <summary>
/// Deterministic audio-level descriptors for one shot, derived from <c>WavRmsSampler</c> windows
/// over the (already-extracted-for-transcription, or freshly-extracted) full-track WAV, plus the
/// already-detected silence spans for <see cref="SpeechRatio"/> — no new audio analysis pass.
/// </summary>
public sealed record VideoAnalysisShotAudio(
    double RmsDbfs,
    double PeakDbfs,
    double SpeechRatio,
    string LoudnessClass);

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
