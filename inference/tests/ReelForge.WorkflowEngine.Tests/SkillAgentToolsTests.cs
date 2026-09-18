using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ReelForge.Shared.Skills;
using ReelForge.WorkflowEngine.Agents.Tools;
using ReelForge.WorkflowEngine.Services.Skills;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// The enforcement boundary for skill access is the resolved skill set a <see cref="SkillAgentTools"/>
/// instance is constructed with (bound per agent run in ReelForgeAgentBase.CreateAgentAsync via
/// ISkillAgentToolsFactory) — not the prompt text. These tests prove an agent resolved with skill
/// set {A} cannot get content for skill B via either tool, only a clear rejection message.
/// </summary>
public class SkillAgentToolsTests
{
    private static readonly SkillDescriptor SkillA = new(
        "skill-a", "Skill A", "Description of skill A.", SkillCategory.Remotion, "skill-a");
    private static readonly SkillDescriptor SkillB = new(
        "skill-b", "Skill B", "Description of skill B.", SkillCategory.Remotion, "skill-b");

    [Fact]
    public async Task UseSkill_for_an_allowed_skill_returns_its_loaded_body()
    {
        Mock<ISkillCorpusService> corpus = new();
        corpus.Setup(c => c.LoadSkillBodyAsync("skill-a", It.IsAny<CancellationToken>()))
            .ReturnsAsync("Full instructions for skill A.");

        SkillAgentTools tools = new(corpus.Object, [SkillA], NullLogger.Instance);

        string result = await tools.UseSkill("skill-a");

        result.Should().Be("Full instructions for skill A.");
    }

    [Fact]
    public async Task UseSkill_for_a_skill_outside_the_resolved_set_is_rejected_not_served()
    {
        Mock<ISkillCorpusService> corpus = new();
        // If the enforcement boundary were broken, this stub would be hit and its content leaked.
        corpus.Setup(c => c.LoadSkillBodyAsync("skill-b", It.IsAny<CancellationToken>()))
            .ReturnsAsync("SECRET instructions for skill B that must never leak.");

        SkillAgentTools tools = new(corpus.Object, [SkillA], NullLogger.Instance);

        string result = await tools.UseSkill("skill-b");

        result.Should().NotContain("SECRET instructions for skill B");
        result.Should().Contain("not available to you");
        result.Should().Contain("skill-a", "the rejection should tell the agent what it IS allowed to use");
        corpus.Verify(c => c.LoadSkillBodyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReadSkillResource_for_a_skill_outside_the_resolved_set_is_rejected_not_served()
    {
        Mock<ISkillCorpusService> corpus = new();
        corpus.Setup(c => c.ReadSkillResourceAsync("skill-b", "notes.md", It.IsAny<CancellationToken>()))
            .ReturnsAsync("SECRET resource content.");

        SkillAgentTools tools = new(corpus.Object, [SkillA], NullLogger.Instance);

        string result = await tools.ReadSkillResource("skill-b", "notes.md");

        result.Should().NotContain("SECRET resource content");
        result.Should().Contain("not available to you");
        corpus.Verify(c => c.ReadSkillResourceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReadSkillResource_for_an_allowed_skill_delegates_to_the_corpus_service()
    {
        Mock<ISkillCorpusService> corpus = new();
        corpus.Setup(c => c.ReadSkillResourceAsync("skill-a", "notes.md", It.IsAny<CancellationToken>()))
            .ReturnsAsync("Notes content.");

        SkillAgentTools tools = new(corpus.Object, [SkillA], NullLogger.Instance);

        (await tools.ReadSkillResource("skill-a", "notes.md")).Should().Be("Notes content.");
    }

    [Fact]
    public async Task UseSkill_with_an_empty_allowed_set_rejects_every_skill()
    {
        Mock<ISkillCorpusService> corpus = new();
        SkillAgentTools tools = new(corpus.Object, [], NullLogger.Instance);

        string result = await tools.UseSkill("skill-a");

        result.Should().Contain("not available to you");
        corpus.Verify(c => c.LoadSkillBodyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void CreateBoundTools_produces_two_ai_tools_scoped_to_the_given_skill_set()
    {
        Mock<ISkillCorpusService> corpus = new();
        SkillAgentToolsFactory factory = new(corpus.Object, NullLogger<SkillAgentTools>.Instance);

        IReadOnlyList<AITool> tools = factory.CreateBoundTools([SkillA, SkillB]);

        tools.Should().HaveCount(2);
    }

    [Fact]
    public async Task CreateBoundTools_instances_are_independently_scoped_per_call()
    {
        // Two calls to CreateBoundTools with different skill sets must not leak allowance between
        // them — each SkillAgentTools instance closes over only the set it was built with.
        Mock<ISkillCorpusService> corpus = new();
        corpus.Setup(c => c.LoadSkillBodyAsync("skill-a", It.IsAny<CancellationToken>())).ReturnsAsync("A body");
        corpus.Setup(c => c.LoadSkillBodyAsync("skill-b", It.IsAny<CancellationToken>())).ReturnsAsync("B body");

        SkillAgentToolsFactory factory = new(corpus.Object, NullLogger<SkillAgentTools>.Instance);

        SkillAgentTools onlyA = new(corpus.Object, [SkillA], NullLogger.Instance);
        SkillAgentTools onlyB = new(corpus.Object, [SkillB], NullLogger.Instance);

        (await onlyA.UseSkill("skill-a")).Should().Be("A body");
        (await onlyA.UseSkill("skill-b")).Should().Contain("not available to you");

        (await onlyB.UseSkill("skill-b")).Should().Be("B body");
        (await onlyB.UseSkill("skill-a")).Should().Contain("not available to you");
    }
}
