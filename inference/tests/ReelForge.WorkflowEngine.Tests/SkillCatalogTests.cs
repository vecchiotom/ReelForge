using System;
using System.Linq;
using FluentAssertions;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Skills;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

public class SkillCatalogTests
{
    [Fact]
    public void All_has_exactly_the_five_curated_skills()
    {
        SkillCatalog.All.Should().HaveCount(5);
        SkillCatalog.All.Select(s => s.Name).Should().BeEquivalentTo(
            "remotion-create", "remotion-markup", "remotion-render", "remotion-captions", "remotion-multimedia");
    }

    [Fact]
    public void Find_is_case_insensitive_and_returns_null_for_unknown_names()
    {
        SkillCatalog.Find("remotion-markup").Should().NotBeNull();
        SkillCatalog.Find("REMOTION-MARKUP").Should().NotBeNull();
        SkillCatalog.Find("does-not-exist").Should().BeNull();
        SkillCatalog.Find("").Should().BeNull();
    }

    [Theory]
    [InlineData(AgentType.CodeStructureAnalyzer)]
    [InlineData(AgentType.DependencyAnalyzer)]
    [InlineData(AgentType.ComponentInventoryAnalyzer)]
    [InlineData(AgentType.RouteAndApiAnalyzer)]
    [InlineData(AgentType.StyleAndThemeExtractor)]
    [InlineData(AgentType.RemotionComponentTranslator)]
    [InlineData(AgentType.AnimationStrategyAgent)]
    [InlineData(AgentType.DirectorAgent)]
    [InlineData(AgentType.ScriptwriterAgent)]
    [InlineData(AgentType.AuthorAgent)]
    [InlineData(AgentType.ReviewAgent)]
    [InlineData(AgentType.FileSummarizerAgent)]
    [InlineData(AgentType.Custom)]
    [InlineData(AgentType.ExtractTransform)]
    [InlineData(AgentType.VideoStoryEditor)]
    [InlineData(AgentType.VideoTransform)]
    [InlineData(AgentType.MotionGraphicsPlanner)]
    public void DefaultsFor_returns_only_names_that_exist_in_the_catalog(AgentType agentType)
    {
        string[] catalogNames = SkillCatalog.All.Select(s => s.Name).ToArray();

        foreach (string skillName in SkillCatalog.DefaultsFor(agentType))
        {
            catalogNames.Should().Contain(skillName,
                $"DefaultsFor({agentType}) referenced a skill name not present in SkillCatalog.All");
        }
    }

    [Fact]
    public void DefaultsFor_covers_every_declared_AgentType_without_throwing()
    {
        // Exhaustive sweep: every enum member, not just the ones spelled out in the Theory above
        // (guards against a newly-added AgentType being silently unmapped).
        foreach (AgentType agentType in Enum.GetValues<AgentType>())
        {
            var names = SkillCatalog.DefaultsFor(agentType);
            names.Should().NotBeNull();

            string[] catalogNames = SkillCatalog.All.Select(s => s.Name).ToArray();
            foreach (string name in names)
            {
                catalogNames.Should().Contain(name);
            }
        }
    }

    [Fact]
    public void The_five_agents_that_used_to_get_RemotionSkills_tools_all_get_a_non_empty_skill_set()
    {
        // These are exactly the five AgentToolProvider arms that previously registered
        // RemotionSkillsAgentTools (SearchRemotionSkills/ReadRemotionSkill/ListAllRemotionSkills).
        AgentType[] previouslyWired =
        [
            AgentType.RemotionComponentTranslator,
            AgentType.AnimationStrategyAgent,
            AgentType.AuthorAgent,
            AgentType.ReviewAgent,
            AgentType.MotionGraphicsPlanner
        ];

        foreach (AgentType agentType in previouslyWired)
        {
            SkillCatalog.DefaultsFor(agentType).Should().NotBeEmpty(
                $"{agentType} previously had Remotion-skill tools and should still get a default skill set");
        }
    }

    [Fact]
    public void MotionGraphicsPlanner_includes_remotion_render_for_transparent_asset_rendering()
    {
        SkillCatalog.DefaultsFor(AgentType.MotionGraphicsPlanner).Should().Contain("remotion-render");
    }

    [Fact]
    public void Every_other_AgentType_not_in_the_curated_list_gets_no_skills_by_default()
    {
        SkillCatalog.DefaultsFor(AgentType.DirectorAgent).Should().BeEmpty();
        SkillCatalog.DefaultsFor(AgentType.ScriptwriterAgent).Should().BeEmpty();
        SkillCatalog.DefaultsFor(AgentType.Custom).Should().BeEmpty();
        SkillCatalog.DefaultsFor(AgentType.VideoStoryEditor).Should().BeEmpty();
    }
}
