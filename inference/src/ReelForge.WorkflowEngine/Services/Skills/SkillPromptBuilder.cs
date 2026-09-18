using System.Text;
using ReelForge.Shared.Skills;

namespace ReelForge.WorkflowEngine.Services.Skills;

/// <summary>
/// Builds the short "available skills" advertisement block appended to an agent's system prompt —
/// structurally similar to how a Claude Code-style environment is told about its own available
/// skills: a name plus a one-line description per skill, with the full instructions loaded only on
/// demand via <c>UseSkill</c>. Deliberately terse (~100 tokens per skill, not a paragraph each).
/// </summary>
public static class SkillPromptBuilder
{
    public static string BuildAdvertisement(IReadOnlyList<SkillDescriptor> skills)
    {
        if (skills.Count == 0)
            return string.Empty;

        StringBuilder sb = new();
        sb.Append("\n\n## Skills\n\n");
        sb.Append(
            "You have access to skills containing domain-specific knowledge and capabilities. Each " +
            "skill provides specialized instructions for a specific task. When a task aligns with a " +
            "skill's domain, call UseSkill with its exact name to load its full instructions before " +
            "proceeding. Only load a skill when you actually need it.\n\n");
        sb.Append("Available skills:\n");

        foreach (SkillDescriptor skill in skills)
        {
            sb.Append("- ").Append(skill.Name).Append(": ").Append(skill.Description).Append('\n');
        }

        return sb.ToString().TrimEnd('\n');
    }
}
