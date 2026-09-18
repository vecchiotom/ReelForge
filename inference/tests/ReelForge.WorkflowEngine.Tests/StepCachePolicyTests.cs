using System;
using FluentAssertions;
using ReelForge.Shared.Data.Models;
using ReelForge.WorkflowEngine.Execution.Caching;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Coverage for <see cref="StepCachePolicy.IsCacheable"/>: the per-StepType decision table, the
/// per-step <see cref="StepCacheMode"/> override, and the Agent-step tool-scope check that reads
/// <see cref="ReelForge.Shared.Agents.ToolGroupCatalog"/> directly (so it can never drift from what
/// <c>AgentToolProvider</c> actually grants at runtime).
/// </summary>
public class StepCachePolicyTests
{
    private static readonly IStepCachePolicy Policy = new StepCachePolicy();

    private static WorkflowStep AgentStep(AgentType agentType, StepCacheMode? cacheMode = null) => new()
    {
        StepType = StepType.Agent,
        CacheMode = cacheMode,
        AgentDefinition = new AgentDefinition { AgentType = agentType }
    };

    // -----------------------------------------------------------------
    // Per-StepType decision table (deterministic non-Agent step types)
    // -----------------------------------------------------------------

    [Theory]
    [InlineData(StepType.Extract, true)]
    [InlineData(StepType.VideoAnalyze, true)]
    [InlineData(StepType.VideoCompile, true)]
    [InlineData(StepType.EditRoom, true)]
    [InlineData(StepType.ColorGradeRoom, true)]
    [InlineData(StepType.GraphicsRoom, false)]
    [InlineData(StepType.Conditional, false)]
    [InlineData(StepType.ForEach, false)]
    [InlineData(StepType.Parallel, false)]
    [InlineData(StepType.ReviewLoop, false)]
    public void IsCacheable_matches_the_default_StepType_table(StepType stepType, bool expected)
    {
        var step = new WorkflowStep
        {
            StepType = stepType,
            // Deterministic step types carry the placeholder agent row; give them one so a naive
            // future implementation that accidentally reads AgentDefinition for these types doesn't
            // NRE instead of reaching the intended StepType arm.
            AgentDefinition = new AgentDefinition { AgentType = AgentType.VideoTransform }
        };

        Policy.IsCacheable(step).Should().Be(expected);
    }

    [Fact]
    public void ReviewLoop_is_never_cacheable_even_with_a_normally_cacheable_agent_type()
    {
        var step = new WorkflowStep
        {
            StepType = StepType.ReviewLoop,
            AgentDefinition = new AgentDefinition { AgentType = AgentType.VideoReviewAgent }
        };

        Policy.IsCacheable(step).Should().BeFalse(
            because: "a ReviewLoop step's whole purpose is to re-judge FRESH output — serving a stale verdict would defeat it");
    }

    [Fact]
    public void GraphicsRoom_is_never_cacheable()
    {
        var step = new WorkflowStep
        {
            StepType = StepType.GraphicsRoom,
            AgentDefinition = new AgentDefinition { AgentType = AgentType.VideoTransform }
        };

        Policy.IsCacheable(step).Should().BeFalse(
            because: "its director/seats may render a real overlay asset as a side effect of producing their output");
    }

    // -----------------------------------------------------------------
    // Agent-step tool-scope check
    // -----------------------------------------------------------------

    [Theory]
    [InlineData(AgentType.AuthorAgent)]
    [InlineData(AgentType.RemotionComponentTranslator)]
    [InlineData(AgentType.MotionGraphicsPlanner)]
    [InlineData(AgentType.MotionGraphicsDirector)]
    public void Agent_steps_with_state_mutating_tool_grants_are_not_cacheable(AgentType agentType)
    {
        Policy.IsCacheable(AgentStep(agentType)).Should().BeFalse(
            because: $"{agentType} is granted at least one of ProjectWrite/SandboxAuthoring/SandboxRender");
    }

    [Theory]
    [InlineData(AgentType.CodeStructureAnalyzer)]
    [InlineData(AgentType.ReviewAgent)]
    [InlineData(AgentType.VideoStoryEditor)]
    [InlineData(AgentType.Colorist)]
    [InlineData(AgentType.MusicSupervisor)]
    [InlineData(AgentType.SoundDesigner)]
    public void Agent_steps_with_read_only_tool_grants_are_cacheable(AgentType agentType)
    {
        Policy.IsCacheable(AgentStep(agentType)).Should().BeTrue(
            because: $"{agentType} is granted none of ProjectWrite/SandboxAuthoring/SandboxRender");
    }

    // -----------------------------------------------------------------
    // Per-step CacheMode override
    // -----------------------------------------------------------------

    [Fact]
    public void CacheMode_Never_overrides_an_otherwise_cacheable_step()
    {
        var step = new WorkflowStep
        {
            StepType = StepType.Extract,
            CacheMode = StepCacheMode.Never,
            AgentDefinition = new AgentDefinition { AgentType = AgentType.ExtractTransform }
        };

        Policy.IsCacheable(step).Should().BeFalse();
    }

    [Fact]
    public void CacheMode_Always_overrides_an_otherwise_uncacheable_StepType()
    {
        var step = new WorkflowStep
        {
            StepType = StepType.ReviewLoop,
            CacheMode = StepCacheMode.Always,
            AgentDefinition = new AgentDefinition { AgentType = AgentType.VideoReviewAgent }
        };

        Policy.IsCacheable(step).Should().BeTrue();
    }

    [Fact]
    public void CacheMode_Always_overrides_an_otherwise_uncacheable_Agent_tool_grant()
    {
        var step = AgentStep(AgentType.AuthorAgent, StepCacheMode.Always);

        Policy.IsCacheable(step).Should().BeTrue();
    }

    [Fact]
    public void CacheMode_Never_overrides_GraphicsRoom()
    {
        var step = new WorkflowStep
        {
            StepType = StepType.GraphicsRoom,
            CacheMode = StepCacheMode.Never,
            AgentDefinition = new AgentDefinition { AgentType = AgentType.VideoTransform }
        };

        Policy.IsCacheable(step).Should().BeFalse();
    }

    [Fact]
    public void CacheMode_Default_falls_through_to_the_built_in_table()
    {
        var step = new WorkflowStep
        {
            StepType = StepType.Extract,
            CacheMode = StepCacheMode.Default,
            AgentDefinition = new AgentDefinition { AgentType = AgentType.ExtractTransform }
        };

        Policy.IsCacheable(step).Should().BeTrue();
    }

    [Fact]
    public void Null_CacheMode_behaves_the_same_as_Default()
    {
        var step = new WorkflowStep
        {
            StepType = StepType.Extract,
            CacheMode = null,
            AgentDefinition = new AgentDefinition { AgentType = AgentType.ExtractTransform }
        };

        Policy.IsCacheable(step).Should().BeTrue();
    }
}
