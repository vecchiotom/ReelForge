namespace ReelForge.Shared.Skills;

/// <summary>
/// Describes one named skill: its identity, where its content lives on disk, and — most
/// importantly — the advertisement text shown to a model deciding whether to load it.
/// </summary>
/// <param name="Name">
/// Slug identifying this skill, matching the directory name under the skills corpus root
/// (<c>Skills:CorpusPath</c>), e.g. <c>"remotion-markup"</c>. This is the exact string an agent
/// must pass to the <c>UseSkill</c>/<c>ReadSkillResource</c> tools.
/// </param>
/// <param name="DisplayName">Human-readable name, for UI/diagnostics only.</param>
/// <param name="Description">
/// The advertisement text shown to the model in its system prompt (see
/// <c>SkillPromptBuilder</c>) — this is the load-bearing field: a model decides whether to call
/// <c>UseSkill</c> based on how well this text matches its current task, so it must be genuinely
/// useful for pattern-matching, not just a category label.
/// </param>
/// <param name="Category">Broad grouping, informational only — see <see cref="SkillCategory"/>.</param>
/// <param name="RelativePath">
/// Directory name under the skills corpus root containing this skill's <c>SKILL.md</c> and any
/// supplementary resource files. Currently always equal to <see cref="Name"/> for the five
/// vendored Remotion skills, but kept as its own field since a future skill's directory name
/// need not match its slug.
/// </param>
/// <param name="Version">Optional version string, parsed from the skill's own SKILL.md frontmatter when present.</param>
public sealed record SkillDescriptor(
    string Name,
    string DisplayName,
    string Description,
    SkillCategory Category,
    string RelativePath,
    string? Version = null);
