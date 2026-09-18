namespace ReelForge.Shared.Data.OutputSchemas;

/// <summary>
/// Structured output for RemotionComponentTranslatorAgent.
/// The agent builds the Remotion project structure directly inside the sandbox
/// using sandbox tools; this schema summarises what was constructed.
/// </summary>
public class RemotionProjectBuildOutput
{
    /// <summary>Relative sandbox paths of every new TSX/TS file written (e.g. "src/LoginScreen.tsx").</summary>
    public List<string> CreatedFiles { get; set; } = new();

    /// <summary>Relative sandbox paths of existing files that were modified (e.g. "src/index.ts", "src/root.tsx").</summary>
    public List<string> ModifiedFiles { get; set; } = new();

    /// <summary>npm packages installed in addition to the template defaults (e.g. ["@mantine/core"]).</summary>
    public List<string> InstalledPackages { get; set; } = new();

    /// <summary>Remotion composition IDs registered in root.tsx (must match registerRoot / Composition id props).</summary>
    public List<string> RegisteredCompositions { get; set; } = new();

    /// <summary>Whether TypeScript type-checking passed with no errors after writing all files.</summary>
    public bool TypeCheckPassed { get; set; }

    /// <summary>Short human-readable description of what was built and any caveats.</summary>
    public string Summary { get; set; } = string.Empty;
}

/// <summary>
/// Structured output for AnimationStrategyAgent
/// </summary>
public class AnimationStrategyOutput
{
    public List<Scene> Scenes { get; set; } = new();
    public int TotalDurationInFrames { get; set; }
    public int Fps { get; set; } = 30;
    public PacingRecommendations Pacing { get; set; } = new();
}

public class Scene
{
    public string Id { get; set; } = string.Empty;
    public string ComponentName { get; set; } = string.Empty;
    public int StartFrame { get; set; }
    public int DurationInFrames { get; set; }
    public string TransitionType { get; set; } = "fade";
    public int TransitionDurationInFrames { get; set; } = 15;
    public List<AnimationElement> Animations { get; set; } = new();
}

public class AnimationElement
{
    public string ElementId { get; set; } = string.Empty;
    public string AnimationType { get; set; } = string.Empty;
    public int StartFrame { get; set; }
    public int DurationInFrames { get; set; }
    public Dictionary<string, object> Parameters { get; set; } = new();
}

public class PacingRecommendations
{
    public string OverallTone { get; set; } = string.Empty;
    public int AverageSceneDuration { get; set; }
    public List<string> PacingNotes { get; set; } = new();
}

// ============================================================================
// ANALYSIS AGENT OUTPUT SCHEMAS
// ============================================================================

/// <summary>
/// Structured output for CodeStructureAnalyzerAgent
/// </summary>
public class CodeStructureOutput
{
    public string ProjectType { get; set; } = string.Empty;
    public string Framework { get; set; } = string.Empty;
    public List<DirectoryInfo> Directories { get; set; } = new();
    public List<string> EntryPoints { get; set; } = new();
    public string OverallArchitecture { get; set; } = string.Empty;
}

public class DirectoryInfo
{
    public string Path { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public int FileCount { get; set; }
}

/// <summary>
/// Structured output for DependencyAnalyzerAgent
/// </summary>
public class DependencyAnalysisOutput
{
    public List<Dependency> Dependencies { get; set; } = new();
    public List<string> DevDependencies { get; set; } = new();
    public string PackageManager { get; set; } = string.Empty;
    public List<DependencyRecommendation> Recommendations { get; set; } = new();
}

public class Dependency
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public bool IsCore { get; set; }
}

public class DependencyRecommendation
{
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Structured output for ComponentInventoryAnalyzerAgent
/// </summary>
public class ComponentInventoryOutput
{
    public List<ComponentInfo> Components { get; set; } = new();
    public int TotalComponents { get; set; }
    public List<string> CommonPatterns { get; set; } = new();
}

public class ComponentInfo
{
    public string Name { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public List<PropInfo> Props { get; set; } = new();
    public string Responsibility { get; set; } = string.Empty;
    public List<string> Dependencies { get; set; } = new();
}

public class PropInfo
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool Required { get; set; }
    public string? DefaultValue { get; set; }
}

/// <summary>
/// Structured output for RouteAndApiAnalyzerAgent
/// </summary>
public class RouteAndApiOutput
{
    public List<RouteInfo> ClientRoutes { get; set; } = new();
    public List<ApiEndpoint> ApiEndpoints { get; set; } = new();
    public string RoutingStrategy { get; set; } = string.Empty;
}

public class RouteInfo
{
    public string Path { get; set; } = string.Empty;
    public string ComponentName { get; set; } = string.Empty;
    public List<string> Parameters { get; set; } = new();
    public bool RequiresAuth { get; set; }
}

public class ApiEndpoint
{
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public List<string> Parameters { get; set; } = new();
}

/// <summary>
/// Structured output for StyleAndThemeExtractorAgent
/// </summary>
public class StyleAndThemeOutput
{
    public ColorPalette Colors { get; set; } = new();
    public Typography Typography { get; set; } = new();
    public Spacing Spacing { get; set; } = new();
    public List<string> ComponentStyles { get; set; } = new();
    public string StylingApproach { get; set; } = string.Empty;
}

public class ColorPalette
{
    public string Primary { get; set; } = string.Empty;
    public string Secondary { get; set; } = string.Empty;
    public string Background { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public Dictionary<string, string> Additional { get; set; } = new();
}

public class Typography
{
    public string PrimaryFont { get; set; } = string.Empty;
    public string SecondaryFont { get; set; } = string.Empty;
    public Dictionary<string, string> FontSizes { get; set; } = new();
}

public class Spacing
{
    public string Unit { get; set; } = string.Empty;
    public Dictionary<string, string> Scale { get; set; } = new();
}

// ============================================================================
// PRODUCTION AGENT OUTPUT SCHEMAS
// ============================================================================

/// <summary>
/// Structured output for DirectorAgent
/// </summary>
public class DirectorOutput
{
    public List<Shot> Shots { get; set; } = new();
    public VisualTheme VisualTheme { get; set; } = new();
    public AudioGuidance Audio { get; set; } = new();
    public int TotalDurationInSeconds { get; set; }
}

public class Shot
{
    public string ShotId { get; set; } = string.Empty;
    public string SceneId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int StartTime { get; set; }
    public int Duration { get; set; }
    public CameraDirection Camera { get; set; } = new();
    public List<string> VisualElements { get; set; } = new();
}

public class CameraDirection
{
    public string Angle { get; set; } = string.Empty;
    public string Movement { get; set; } = string.Empty;
    public string Focus { get; set; } = string.Empty;
}

public class VisualTheme
{
    public string Mood { get; set; } = string.Empty;
    public string ColorGrading { get; set; } = string.Empty;
    public List<string> VisualMotifs { get; set; } = new();
}

public class AudioGuidance
{
    public string MusicStyle { get; set; } = string.Empty;
    public string SoundEffects { get; set; } = string.Empty;
    public string Voiceover { get; set; } = string.Empty;
}

/// <summary>
/// Structured output for ScriptwriterAgent
/// </summary>
public class ScriptwriterOutput
{
    public string Title { get; set; } = string.Empty;
    public int DurationInSeconds { get; set; }
    public List<ScriptScene> Scenes { get; set; } = new();
    public string Narrative { get; set; } = string.Empty;
}

public class ScriptScene
{
    public string SceneId { get; set; } = string.Empty;
    public int StartTime { get; set; }
    public int Duration { get; set; }
    public string Voiceover { get; set; } = string.Empty;
    public List<string> OnScreenText { get; set; } = new();
    public string VisualDescription { get; set; } = string.Empty;
}

/// <summary>
/// Structured output for AuthorAgent (RenderManifest)
/// </summary>
public class RenderManifestOutput
{
    public string ProjectName { get; set; } = string.Empty;
    public VideoConfiguration Video { get; set; } = new();
    public List<Composition> Compositions { get; set; } = new();
    public List<AssetReference> Assets { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class VideoConfiguration
{
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public int Fps { get; set; } = 30;
    public int DurationInFrames { get; set; }
}

public class Composition
{
    public string Id { get; set; } = string.Empty;
    public string ComponentName { get; set; } = string.Empty;
    public int DurationInFrames { get; set; }
    public Dictionary<string, object> Props { get; set; } = new();
    public CompositionScript Script { get; set; } = new();
}

public class CompositionScript
{
    public string Voiceover { get; set; } = string.Empty;
    public List<string> Captions { get; set; } = new();
}

public class AssetReference
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public Dictionary<string, object> Properties { get; set; } = new();
}

// ============================================================================
// QUALITY AGENT OUTPUT SCHEMAS
// ============================================================================

/// <summary>
/// Structured output for ReviewAgent
/// </summary>
public class ReviewOutput
{
    public int OverallScore { get; set; }
    public List<ReviewCriterion> Criteria { get; set; } = new();
    public List<string> Strengths { get; set; } = new();
    public List<string> ImprovementAreas { get; set; } = new();
    public bool PassesReview { get; set; }
    public string Summary { get; set; } = string.Empty;
}

public class ReviewCriterion
{
    public string Name { get; set; } = string.Empty;
    public int Score { get; set; }
    public string Feedback { get; set; } = string.Empty;
}

/// <summary>
/// Structured output for AgentType.VideoReviewAgent — the video-editing pipelines' ReviewLoop
/// review, mirroring ReviewOutput's role for the main promo pipeline but judging the compiled
/// EDIT rather than code/lint quality. Deliberately a separate, smaller schema rather than
/// reusing ReviewOutput: the criteria this agent actually has evidence for (a deterministic
/// sentence-boundary check and overlay frame-coverage numbers VideoCompileStepExecutor already
/// computed) don't map onto ReviewOutput's narrativeClarity/visualAccuracy/timing/completeness
/// criteria, which are meaningless for a video edit with no Remotion code to inspect.
/// </summary>
/// <remarks>
/// The top-level property is deliberately named <see cref="Score"/> (serializing to "score"),
/// not "overallScore" — ReviewLoopStepExecutor.ParseReviewScore checks "score" first, and the
/// main pipeline's ReviewOutput.OverallScore actually serializes as "overallScore", a pre-existing
/// mismatch that meant the main pipeline's review score silently always parsed as 0 (found while
/// wiring this agent's own score through the same method — fixed there by also accepting
/// "overallScore" as a fallback, but this type is named correctly from the start regardless).
/// </remarks>
public class VideoReviewOutput
{
    public int Score { get; set; }
    public bool PassesReview { get; set; }
    public List<string> Issues { get; set; } = new();
    public List<string> Strengths { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
}

// ============================================================================
// FILE PROCESSING AGENT OUTPUT SCHEMAS
// ============================================================================

/// <summary>
/// Structured output for FileSummarizerAgent.
/// </summary>
public class FileSummaryOutput
{
    public string FileType { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<string> KeyPoints { get; set; } = new();
    public string VideoRelevance { get; set; } = string.Empty;
    public List<string> NotablePatterns { get; set; } = new();
}

// ============================================================================
// VIDEO EDITING AGENT OUTPUT SCHEMAS
// ============================================================================

/// <summary>
/// A single contiguous run of offered ids to keep, inclusive on both ends.
/// </summary>
/// <remarks>
/// THE RUSHCUT INVARIANT: this class, and <see cref="VideoEditDecisionOutput"/> as a whole,
/// must never gain a numeric or time-bearing property (no double/int-as-seconds-or-frames, no
/// TimeSpan, no DateTime). The model is never trusted with a timestamp — its only contribution
/// to the cut list is a set of opaque string ids drawn from the set it was actually shown
/// (VideoAnalysisArtifact.OfferedIds). Resolving ids to frame-accurate times is
/// VideoCompileStepExecutor's job, done entirely from the full analysis artifact. A future
/// "helpful" addition of e.g. StartSec here would defeat the entire point of this contract —
/// see the reflection test asserting this type is structurally incapable of expressing a time.
/// </remarks>
public class VideoEditKeepSpan
{
    /// <summary>First offered id to keep, inclusive. e.g. "t7" or "s2".</summary>
    public string FromId { get; set; } = string.Empty;

    /// <summary>Last offered id to keep, inclusive. Must be >= FromId in the offered order.</summary>
    public string ToId { get; set; } = string.Empty;

    /// <summary>Why this span stays. Prose only.</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Structured output for VideoStoryEditorAgent. Single representation only (<see cref="Keep"/>)
/// — there is deliberately no "remove" list, since two ways to express the same edit would
/// create a resolution order to get wrong. Everything not covered by a kept span is cut.
/// </summary>
public class VideoEditDecisionOutput
{
    /// <summary>
    /// Ordered, strictly increasing, non-overlapping spans of offered ids to keep.
    /// </summary>
    public List<VideoEditKeepSpan> Keep { get; set; } = new();

    public string EditRationale { get; set; } = string.Empty;

    public string SuggestedTitle { get; set; } = string.Empty;
}

/// <summary>
/// One planned motion-graphics overlay (lower-third, title, callout, tag). Structured output for
/// <c>MotionGraphicsPlannerAgent</c> (Phase 3 — see docs/video-editing.md "Motion graphics").
/// </summary>
/// <remarks>
/// THE SAME RUSHCUT INVARIANT, extended to graphics: this class, and
/// <see cref="MotionGraphicsPlanOutput"/> as a whole, must never gain a numeric or time-bearing
/// property, and must never carry a pixel coordinate. The model's only contribution is an opaque
/// <see cref="PlacementId"/> drawn from the set it was actually offered
/// (<c>VideoAnalysisArtifact.OfferedPlacementIds</c>) plus enum-word choices
/// (<see cref="Duration"/>/<see cref="Emphasis"/>) that <c>VideoCompileStepExecutor</c> alone
/// resolves to milliseconds/style — never a number the model supplied directly. See the
/// reflection test asserting this type is structurally incapable of expressing a time or a
/// position.
/// </remarks>
public class MotionGraphicsOverlay
{
    /// <summary>Must be one of the ids in <c>VideoAnalysisArtifact.OfferedPlacementIds</c> — never invented.</summary>
    public string PlacementId { get; set; } = string.Empty;

    /// <summary>
    /// One of: LowerThird | Title | Callout | Tag.
    /// </summary>
    /// <remarks>
    /// Currently DESCRIPTIVE/RESERVED ONLY: <c>DrawtextFilterBuilder</c> does not read this value
    /// at all — every <see cref="Kind"/> renders identically (position/size come entirely from the
    /// chosen placement's <c>Rect</c> and from <see cref="Emphasis"/>, not from <see cref="Kind"/>).
    /// A per-kind rendering difference was considered during the audit cleanup pass but rejected as
    /// out of scope for a small change: the placement's region (LowerThird/UpperThird/CenterBand) is
    /// already resolved server-side from <c>view.placements</c>, so having <see cref="Kind"/> ALSO
    /// influence position would create two disagreeing sources of geometry for the same overlay.
    /// Reserved for a future rendering differentiation (e.g. a distinct style per kind) should that
    /// be designed deliberately. See docs/video-editing.md "Motion graphics (Phase 3)".
    /// </remarks>
    public string Kind { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public string Subtext { get; set; } = string.Empty;

    /// <summary>
    /// One of: Short | Medium | Hold — never a number. Mapped to actual milliseconds
    /// (<c>VideoCompileStepConfig.OverlayShortMs</c>/<c>OverlayMediumMs</c>/<c>OverlayHoldMs</c>)
    /// entirely server-side.
    /// </summary>
    public string Duration { get; set; } = string.Empty;

    /// <summary>One of: Subtle | Normal | Strong.</summary>
    public string Emphasis { get; set; } = string.Empty;

    /// <summary>Why this overlay was chosen. Prose only.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Optional: the S3 storage key of a rendered, transparent-background motion-graphics asset
    /// this agent produced ITSELF by actually calling <c>RenderVideoAndUploadToStorage</c> — a
    /// real tool call that performs a real render and a real S3 upload, unlike <see cref="PlacementId"/>
    /// which is merely echoed back from a set the model was shown. Still not trusted blindly:
    /// <c>VideoCompileStepExecutor</c> validates this key matches the exact
    /// <c>projects/{projectId}/outputFiles/{executionId}/...</c> prefix
    /// <c>RenderVideoAndUploadToStorage</c> itself constructs for the CURRENT execution before
    /// downloading or compositing anything at this key — the same prefix-validation discipline
    /// <c>StepResultArtifactsController</c> already applies to <c>ArtifactStorageKey</c>.
    /// </summary>
    /// <remarks>
    /// When non-empty, this overlay is composited via ffmpeg's <c>overlay</c> filter (the asset is
    /// added as an extra input, scaled to the resolved placement box, and alpha-blended) INSTEAD
    /// of the plain-text drawbox/drawtext path — <see cref="Text"/>/<see cref="Subtext"/> are
    /// ignored for an overlay that carries a rendered asset. Empty/absent (the default) leaves
    /// this overlay a plain text overlay exactly as before this field existed — additive, not a
    /// replacement. An overlay is one or the other, never both: author two separate
    /// <see cref="MotionGraphicsOverlay"/> entries (at different placements) to combine a
    /// rendered graphic with separate caption text.
    /// </remarks>
    public string RenderedAssetStorageKey { get; set; } = string.Empty;
}

/// <summary>
/// One planned tracked screen insert: a Remotion-rendered scene composited INTO a tracked
/// chroma-plate region of the source footage (e.g. an app UI inserted into a phone's green-screen
/// area, warping with the phone as the hand moves) — see docs/video-editing.md "Tracked screen
/// inserts (Phase 5)".
/// </summary>
/// <remarks>
/// THE SAME RUSHCUT INVARIANT, extended to tracked compositing: this class must never gain a
/// numeric, time-bearing, or coordinate-bearing property. Motion tracking is inherently per-frame
/// numeric data — every one of those numbers is computed by deterministic C#
/// (<c>ChromaQuadTracker</c>) and consumed by deterministic C#
/// (<c>VideoCompileStepExecutor</c>/<c>ScreenInsertFilterBuilder</c>); the model's only
/// contribution is an opaque <see cref="RegionId"/> drawn from the set it was actually offered
/// (<c>VideoAnalysisArtifact.OfferedInsertRegionIds</c>) plus a rendered asset it produced itself
/// via a real <c>RenderVideoAndUploadToStorage</c> call (the exact
/// <see cref="MotionGraphicsOverlay.RenderedAssetStorageKey"/> precedent, validated the same way).
/// See <c>MotionGraphicsPlanOutputInvariantTests</c>.
/// </remarks>
public class ScreenInsert
{
    /// <summary>Must be one of the ids in <c>VideoAnalysisArtifact.OfferedInsertRegionIds</c> (e.g. "r0") — never invented.</summary>
    public string RegionId { get; set; } = string.Empty;

    /// <summary>
    /// REQUIRED (unlike an overlay, an insert has no plain-text fallback): the storage key of the
    /// rendered scene to composite into the tracked region, produced by a real
    /// <c>RenderVideoAndUploadToStorage</c> call in THIS run. Validated against this execution's
    /// own <c>projects/{projectId}/outputFiles/{executionId}/...</c> prefix before anything is
    /// downloaded or composited — same discipline as
    /// <see cref="MotionGraphicsOverlay.RenderedAssetStorageKey"/>.
    /// </summary>
    public string RenderedAssetStorageKey { get; set; } = string.Empty;

    /// <summary>Why this insert was chosen. Prose only.</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Structured output for <c>MotionGraphicsPlannerAgent</c>. Zero or more overlays, each anchored
/// only to a placement id offered by a <c>StepType.VideoAnalyze</c> step
/// (<c>view.placements</c>) — never a timestamp or pixel coordinate anywhere in this type — plus
/// zero or more tracked screen inserts anchored only to offered insert-region ids
/// (<c>view.insertRegions</c>).
/// </summary>
public class MotionGraphicsPlanOutput
{
    public List<MotionGraphicsOverlay> Overlays { get; set; } = new();

    /// <summary>
    /// Tracked screen inserts (docs/video-editing.md "Tracked screen inserts (Phase 5)"). Empty by
    /// default and for every plan produced before this field existed — additive, never required.
    /// Only applied when <c>VideoCompileStepConfig.EnableInserts</c> is on.
    /// </summary>
    public List<ScreenInsert> Inserts { get; set; } = new();

    public string PlanRationale { get; set; } = string.Empty;
}

// ============================================================================
// BACKGROUND MUSIC AGENT OUTPUT SCHEMA (see docs/video-editing.md "Background music")
// ============================================================================

/// <summary>
/// Structured output for <c>MusicSupervisorAgent</c>. Flat, single-track, no list: one background
/// bed per edit in v1 (no cue sheet, no per-section music). Every property is a plain string, so
/// the same rushcut invariant <see cref="VideoEditDecisionOutput"/>/<see cref="MotionGraphicsPlanOutput"/>
/// established holds here by construction — there is no numeric/time-bearing CLR type to even ban.
/// </summary>
/// <remarks>
/// The model's only contribution is an opaque <see cref="TrackId"/> drawn from the set it was
/// actually offered (<c>VideoAnalysisArtifact.OfferedMusicIds</c>) plus enum-word choices
/// (<see cref="Intensity"/>/<see cref="Ducking"/>/<see cref="Fit"/>) that
/// <c>VideoCompileStepExecutor</c> alone resolves to dB levels/ffmpeg behavior — never a dB value,
/// a volume, a level, a percentage, or a timestamp/duration in seconds anywhere in this type. See
/// <c>MusicPlanOutputInvariantTests</c>.
/// </remarks>
public class MusicPlanOutput
{
    /// <summary>Must be one of the ids in <c>VideoAnalysisArtifact.OfferedMusicIds</c> — never invented. Empty when no track suits the edit.</summary>
    public string TrackId { get; set; } = string.Empty;

    /// <summary>One of: Quiet | Balanced | Feature. Never a dB number — mapped to a bed level entirely server-side.</summary>
    public string Intensity { get; set; } = string.Empty;

    /// <summary>One of: Off | Light | Normal | Heavy. Never a dB number — mapped to an attenuation entirely server-side.</summary>
    public string Ducking { get; set; } = string.Empty;

    /// <summary>One of: LoopToFit | PlayOnce.</summary>
    public string Fit { get; set; } = string.Empty;

    /// <summary>Why this track/settings were chosen. Prose only.</summary>
    public string Reason { get; set; } = string.Empty;

    public string PlanRationale { get; set; } = string.Empty;
}
