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
    public int StartFrame { get; set; }
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
