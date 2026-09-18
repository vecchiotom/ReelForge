using ReelForge.Shared.Data.Models;

namespace ReelForge.Shared.Skills;

/// <summary>Grouping for a <see cref="SkillDescriptor"/>. Only one value exists today.</summary>
public enum SkillCategory
{
    Remotion,
}

/// <summary>Static metadata describing one assignable skill.</summary>
public sealed record SkillDescriptor(
    string Name,
    string DisplayName,
    string Description,
    SkillCategory Category,
    string RelativePath,
    int Version = 1);

/// <summary>
/// Code-defined source of truth for WHICH skills exist and WHICH built-in agents get them by
/// default. See <c>docs/video-editing.md</c> / the "Remotion Knowledge Base" replacement design
/// for the full rationale — this deliberately mirrors Microsoft Agent Framework's own Agent
/// Skills <c>SKILL.md</c> + YAML-frontmatter format (and the pattern any Claude Code-style
/// environment uses for its own skills), built natively rather than by adopting
/// <c>Microsoft.SemanticKernel</c> or MAF's <c>AgentSkillsProvider</c> class.
/// </summary>
public static class SkillCatalog
{
    /// <summary>
    /// Exactly five curated skills — deliberately narrow. The upstream remotion-dev/skills
    /// repository's full catalog includes SaaS-platform building, interactive Studio UI,
    /// version-upgrade guides, and general docs indexing, none of which applies to agents doing
    /// automated, headless Remotion codegen for a promotional-video pipeline. See
    /// inference/skills/UPSTREAM.md for exactly what was vendored and why.
    /// </summary>
    public static IReadOnlyList<SkillDescriptor> All { get; } =
    [
        new SkillDescriptor(
            Name: "remotion-create",
            DisplayName: "Remotion Create",
            Description: "Project and composition scaffolding, Tailwind setup, video layout basics for a new Remotion component.",
            Category: SkillCategory.Remotion,
            RelativePath: "remotion-create"),

        new SkillDescriptor(
            Name: "remotion-markup",
            DisplayName: "Remotion Markup",
            Description: "Core Remotion React markup patterns: timing, sequencing, transitions, audio, 3D, text, animation effects.",
            Category: SkillCategory.Remotion,
            RelativePath: "remotion-markup"),

        new SkillDescriptor(
            Name: "remotion-render",
            DisplayName: "Remotion Render",
            Description: "Render configuration and output, including transparent/alpha-channel video output.",
            Category: SkillCategory.Remotion,
            RelativePath: "remotion-render"),

        new SkillDescriptor(
            Name: "remotion-captions",
            DisplayName: "Remotion Captions",
            Description: "Rendering captions and subtitles in a Remotion composition.",
            Category: SkillCategory.Remotion,
            RelativePath: "remotion-captions"),

        new SkillDescriptor(
            Name: "remotion-multimedia",
            DisplayName: "Remotion Multimedia",
            Description: "Probing and working with media duration/dimensions/metadata in a Remotion composition.",
            Category: SkillCategory.Remotion,
            RelativePath: "remotion-multimedia"),
    ];

    private static readonly IReadOnlyDictionary<string, SkillDescriptor> ByName =
        All.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);

    public static SkillDescriptor? Find(string name) =>
        !string.IsNullOrWhiteSpace(name) && ByName.TryGetValue(name, out SkillDescriptor? descriptor)
            ? descriptor
            : null;

    // Every set below is a plain array of the skill Names above — kept as string[] rather than
    // SkillDescriptor[] so this stays a dependency-free source of truth (no SkillDescriptor
    // construction here), consumed via SkillCatalog.Find by callers.
    private static readonly string[] MarkupOnly = ["remotion-markup"];
    private static readonly string[] CreateAndMarkup = ["remotion-create", "remotion-markup"];
    private static readonly string[] MarkupAndRender = ["remotion-markup", "remotion-render"];
    private static readonly string[] AllFive =
        ["remotion-create", "remotion-markup", "remotion-render", "remotion-captions", "remotion-multimedia"];
    private static readonly string[] None = [];

    /// <summary>
    /// The COMPLETE, non-overridable skill set for a built-in <see cref="AgentType"/>. This is
    /// the ONLY place a built-in agent's skills are determined — there is deliberately no
    /// runtime/DB override for built-in agents. A sibling effort adds a per-Custom-agent DB
    /// override (<c>AgentDefinition.AssignedSkillsJson</c>), but that applies ONLY to Custom
    /// (non-built-in) <c>AgentDefinition</c> rows — a built-in agent's skills come from this
    /// method and the seeded prompt alone, full stop. See
    /// <c>ReelForgeAgentBase.CreateAgentAsync</c> for how the two paths are told apart.
    /// </summary>
    public static IReadOnlyList<string> DefaultsFor(AgentType agentType) => agentType switch
    {
        // Writes TSX component files into the sandbox from scratch — needs scaffolding
        // (composition registration, Tailwind setup) and the core markup patterns it's
        // translating the source app into. It explicitly does not render (AuthorAgent does),
        // so no remotion-render.
        AgentType.RemotionComponentTranslator => CreateAndMarkup,

        // Purely about timing/sequencing/transition design over components someone else wrote —
        // the whole job lives inside remotion-markup's timing/sequencing/transitions/animations
        // topics. No scaffolding, no rendering, no captions/multimedia probing.
        AgentType.AnimationStrategyAgent => MarkupOnly,

        // The final assembler: registers/finalizes compositions (create), composes the whole
        // timeline (markup — sequencing/transitions), is the one agent that actually renders the
        // deliverable mp4 (render), assembles voiceover/caption script content into the manifest
        // (captions), and may need to probe asset duration/dimensions while assembling (multimedia).
        // Gets the full set.
        AgentType.AuthorAgent => AllFive,

        // Reviews the whole pipeline's Remotion output for correctness against best practices
        // across every one of these domains (compositions/timing, rendering, captions, media
        // probing) before scoring — needs the same breadth as AuthorAgent to verify it.
        AgentType.ReviewAgent => AllFive,

        // Optionally renders a small transparent-background overlay asset (lower-third/title/
        // callout) using core markup patterns plus the transparent-render recipe specifically —
        // no project scaffolding (the sandbox/project already exists), no captions/multimedia.
        AgentType.MotionGraphicsPlanner => MarkupAndRender,

        // Every other AgentType (analysis agents, Director/Scriptwriter, the video-editing
        // decision agents, ExtractTransform/VideoTransform placeholders, FileSummarizerAgent,
        // Custom) gets no skills by default.
        _ => None
    };
}
