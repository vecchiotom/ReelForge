using System.Collections.Generic;
using FluentAssertions;
using ReelForge.Shared.Skills;
using ReelForge.WorkflowEngine.Services.Skills;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

public class SkillPromptBuilderTests
{
    [Fact]
    public void BuildAdvertisement_returns_empty_string_for_no_skills()
    {
        SkillPromptBuilder.BuildAdvertisement([]).Should().BeEmpty();
    }

    [Fact]
    public void BuildAdvertisement_produces_the_expected_text_for_a_known_skill_set()
    {
        SkillDescriptor[] skills =
        [
            new SkillDescriptor(
                "remotion-markup", "Remotion Markup",
                "Core Remotion React markup patterns: timing, sequencing, transitions, audio, 3D, text, animation effects.",
                SkillCategory.Remotion, "remotion-markup"),
            new SkillDescriptor(
                "remotion-render", "Remotion Render",
                "Render configuration and output, including transparent/alpha-channel video output.",
                SkillCategory.Remotion, "remotion-render"),
        ];

        string expected =
            "\n\n## Skills\n\n" +
            "You have access to skills containing domain-specific knowledge and capabilities. Each " +
            "skill provides specialized instructions for a specific task. When a task aligns with a " +
            "skill's domain, call UseSkill with its exact name to load its full instructions before " +
            "proceeding. Only load a skill when you actually need it.\n\n" +
            "Available skills:\n" +
            "- remotion-markup: Core Remotion React markup patterns: timing, sequencing, transitions, audio, 3D, text, animation effects.\n" +
            "- remotion-render: Render configuration and output, including transparent/alpha-channel video output.";

        SkillPromptBuilder.BuildAdvertisement(skills).Should().Be(expected);
    }

    [Fact]
    public void BuildAdvertisement_lists_every_skill_name_and_description_once()
    {
        List<SkillDescriptor> skills = [.. SkillCatalog.All];

        string advertisement = SkillPromptBuilder.BuildAdvertisement(skills);

        foreach (SkillDescriptor skill in skills)
        {
            advertisement.Should().Contain($"- {skill.Name}: {skill.Description}");
        }
    }
}
