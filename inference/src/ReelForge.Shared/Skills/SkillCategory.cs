namespace ReelForge.Shared.Skills;

/// <summary>
/// Broad grouping for a <see cref="SkillDescriptor"/>. Not used for access control (that's
/// <see cref="SkillCatalog.DefaultsFor"/>/the per-agent resolved skill set) — purely informational,
/// e.g. for the <c>/skills/status</c> diagnostic endpoint.
/// </summary>
public enum SkillCategory
{
    /// <summary>A skill vendored from the upstream Remotion skills corpus (see inference/skills/).</summary>
    Remotion,

    /// <summary>A ReelForge-authored skill (none yet — reserved for future first-party skills).</summary>
    ReelForge,

    /// <summary>A skill authored by a workflow owner for their own Custom agents.</summary>
    Custom
}
