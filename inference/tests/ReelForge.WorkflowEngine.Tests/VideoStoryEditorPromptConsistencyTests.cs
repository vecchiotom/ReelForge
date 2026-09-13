using System.Collections.Generic;
using System.Reflection;
using FluentAssertions;
using ReelForge.Inference.Api.Data;
using ReelForge.Shared.Data.Models;
using ReelForge.WorkflowEngine.Agents.Production;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// <see cref="VideoStoryEditorAgent"/>'s in-process fallback system prompt and the built-in
/// <c>AgentDefinition</c> row <see cref="DatabaseSeeder"/> seeds for
/// <see cref="AgentType.VideoStoryEditor"/> must stay verbatim-identical — the fallback exists
/// only for the (should-never-happen) case where the seeded config-driven prompt is absent, and a
/// silent drift between the two would mean that fallback describes a different contract (e.g. a
/// stale no-timestamp rule or missing Phase 1 visual/audio field description) than what agents
/// actually run with day to day. Reflection is used since both are intentionally private —
/// neither should widen its access just to satisfy this test.
///
/// <see cref="MotionGraphicsPlannerAgent"/> (Phase 3) gets the exact same verbatim-consistency
/// check, mirroring the same risk for its own no-timestamp/no-coordinate contract.
/// </summary>
public class VideoStoryEditorPromptConsistencyTests
{
    [Fact]
    public void Fallback_prompt_matches_the_seeded_built_in_agent_prompt_verbatim()
    {
        AssertFallbackMatchesSeeded(typeof(VideoStoryEditorAgent), AgentType.VideoStoryEditor);
    }

    [Fact]
    public void MotionGraphicsPlanner_fallback_prompt_matches_the_seeded_built_in_agent_prompt_verbatim()
    {
        AssertFallbackMatchesSeeded(typeof(MotionGraphicsPlannerAgent), AgentType.MotionGraphicsPlanner);
    }

    private static void AssertFallbackMatchesSeeded(System.Type agentType, AgentType builtInAgentType)
    {
        FieldInfo? promptField = agentType.GetField("DefaultPrompt", BindingFlags.NonPublic | BindingFlags.Static);
        promptField.Should().NotBeNull($"{agentType.Name} must expose a private DefaultPrompt constant");
        string fallbackPrompt = (string)promptField!.GetValue(null)!;

        FieldInfo? builtInAgentsField = typeof(DatabaseSeeder)
            .GetField("BuiltInAgents", BindingFlags.NonPublic | BindingFlags.Static);
        builtInAgentsField.Should().NotBeNull("DatabaseSeeder must expose a private BuiltInAgents seed table");

        var builtInAgents = (Dictionary<AgentType, (string Name, string Description, string SystemPrompt, string Color)>)
            builtInAgentsField!.GetValue(null)!;

        builtInAgents.Should().ContainKey(builtInAgentType);
        string seededPrompt = builtInAgents[builtInAgentType].SystemPrompt;

        seededPrompt.Should().Be(fallbackPrompt,
            $"the fallback prompt in {agentType.Name}.cs and the seeded built-in agent row in " +
            "DatabaseSeeder.cs must be kept verbatim-identical");
    }
}
