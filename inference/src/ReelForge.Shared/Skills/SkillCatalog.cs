using ReelForge.Shared.Data.Models;

namespace ReelForge.Shared.Skills;

// STUB — placeholder for a sibling effort's real SkillCatalog (separate worktree, not yet
// merged into this one at the time this file was written). Shape (SkillDescriptor fields,
// SkillCategory, SkillCatalog.All/DefaultsFor/Find) is a best-effort guess at what that effort
// will land, so callers here (SkillsController, AgentDefinitionResponse mapping) compile and are
// testable without blocking on it. RECONCILE WITH THE REAL VERSION AT MERGE TIME — in particular
// the RelativePath values below assume a future vendored corpus under inference/skills/{name}/
// mirroring the five directory names RemotionSkillsService already fetches from GitHub
// (remotion-create, remotion-markup, remotion-render, remotion-captions, remotion-multimedia).

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
/// Static registry of skills that can be assigned to an agent. Built-in agents get their skill
/// set exclusively from <see cref="DefaultsFor"/>; a custom agent's skill set is whatever an
/// admin has explicitly assigned (see AgentDefinition.AssignedSkillsJson) and is validated
/// against <see cref="Find"/>.
/// </summary>
public static class SkillCatalog
{
    public static readonly IReadOnlyList<SkillDescriptor> All =
    [
        new SkillDescriptor(
            "remotion-create",
            "Remotion: Create",
            "Scaffolding a new Remotion project and composition.",
            SkillCategory.Remotion,
            "remotion-create"),
        new SkillDescriptor(
            "remotion-markup",
            "Remotion: Markup",
            "Content, animation, and effects best practices for Remotion React markup.",
            SkillCategory.Remotion,
            "remotion-markup"),
        new SkillDescriptor(
            "remotion-render",
            "Remotion: Render",
            "Exporting/rendering a Remotion video, including transparent output.",
            SkillCategory.Remotion,
            "remotion-render"),
        new SkillDescriptor(
            "remotion-captions",
            "Remotion: Captions",
            "Transcribing, displaying, and animating captions in Remotion.",
            SkillCategory.Remotion,
            "remotion-captions"),
        new SkillDescriptor(
            "remotion-multimedia",
            "Remotion: Multimedia",
            "Reading audio/video duration and dimensions via Mediabunny.",
            SkillCategory.Remotion,
            "remotion-multimedia"),
    ];

    private static readonly IReadOnlyDictionary<string, SkillDescriptor> ByName =
        All.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Looks up a skill by its stable name (case-insensitive). Null if unknown.</summary>
    public static SkillDescriptor? Find(string name) =>
        ByName.TryGetValue(name, out SkillDescriptor? descriptor) ? descriptor : null;

    /// <summary>
    /// The fixed skill set a built-in agent of this type gets from code. Never editable at
    /// runtime — see AgentDefinition.AssignedSkillsJson's doc comment.
    /// </summary>
    public static IReadOnlyList<string> DefaultsFor(AgentType agentType) => agentType switch
    {
        AgentType.RemotionComponentTranslator or
        AgentType.AnimationStrategyAgent or
        AgentType.AuthorAgent or
        AgentType.ReviewAgent or
        AgentType.MotionGraphicsPlanner =>
            [.. All.Select(s => s.Name)],
        _ => [],
    };
}
