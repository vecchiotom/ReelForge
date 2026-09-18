using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Amazon.S3;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ReelForge.Inference.Api.Data;
using ReelForge.Shared.Agents;
using ReelForge.Shared.Data.Models;
using ReelForge.WorkflowEngine.Agents.Tools;
using ReelForge.WorkflowEngine.Execution;
using ReelForge.WorkflowEngine.Services.RemotionSkills;
using ReelForge.WorkflowEngine.Services.Storage;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Regression guard for the exact bug this repo has already hit twice: the WorkflowEngine's real,
/// load-bearing tool grant (<see cref="AgentToolProvider.GetTools"/>) and the Inference API's
/// display-only "Available Tools" metadata (<c>DatabaseSeeder.GetAvailableToolsJson</c>, private —
/// invoked here via reflection, the same pattern <see cref="VideoStoryEditorPromptConsistencyTests"/>
/// uses) used to be two hand-written, independently maintained mappings. They drifted apart twice:
/// once <c>FailWorkflow</c> was silently missing from the display metadata entirely (found by e2e
/// QA), and once <c>MotionGraphicsPlanner</c> was widened in the real tool provider without the
/// corresponding display-metadata update. Both now derive their answer from the single
/// <see cref="ToolGroupCatalog"/> (see <c>inference/src/ReelForge.Shared/Agents/</c>), so this test
/// asserts — for every <see cref="AgentType"/> — that the two sets of tool names are exactly equal.
/// If anyone ever again edits one side's scoping without going through the shared catalog, this
/// test fails immediately instead of silently shipping stale display metadata (or, worse, an
/// under/over-scoped agent) a third time.
/// </summary>
public class ToolScopingDriftGuardTests
{
    public static IEnumerable<object[]> AllAgentTypes() =>
        Enum.GetValues<AgentType>().Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(AllAgentTypes))]
    public void Real_tool_grant_and_display_metadata_agree_on_tool_names(AgentType agentType)
    {
        IReadOnlyList<AIFunction> realTools = CreateAgentToolProvider().GetTools(agentType);
        HashSet<string> realToolNames = realTools.Select(f => f.Name).ToHashSet();

        HashSet<string> displayToolNames = InvokeGetAvailableToolsJson(agentType);

        displayToolNames.Should().BeEquivalentTo(realToolNames,
            $"AgentToolProvider.GetTools({agentType}) and DatabaseSeeder.GetAvailableToolsJson({agentType}) " +
            "must derive from the same ToolGroupCatalog and therefore agree exactly — a mismatch here " +
            "means one side was edited without the other, which is precisely the drift this catalog exists to prevent");
    }

    /// <summary>
    /// Sanity check that this test actually exercises real tool names, not two empty sets agreeing
    /// vacuously, for every agent type that is genuinely expected to receive tools.
    /// </summary>
    [Fact]
    public void Every_LLM_agent_type_is_granted_at_least_FailWorkflow()
    {
        AgentToolProvider provider = CreateAgentToolProvider();

        foreach (AgentType agentType in Enum.GetValues<AgentType>())
        {
            if (agentType is AgentType.ExtractTransform or AgentType.VideoTransform)
                continue;

            IReadOnlyList<AIFunction> tools = provider.GetTools(agentType);
            tools.Select(f => f.Name).Should().Contain("FailWorkflow",
                $"{agentType} is a real (non-deterministic) agent type and must always be able to abort the workflow");
        }
    }

    private static HashSet<string> InvokeGetAvailableToolsJson(AgentType agentType)
    {
        MethodInfo? method = typeof(DatabaseSeeder)
            .GetMethod("GetAvailableToolsJson", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("DatabaseSeeder must expose a private static GetAvailableToolsJson(AgentType) method");

        string json = (string)method!.Invoke(null, [agentType])!;
        string[] names = JsonSerializer.Deserialize<string[]>(json)!;
        return names.ToHashSet();
    }

    private static AgentToolProvider CreateAgentToolProvider()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        ProjectFileAgentTools projectFileTools = new(
            Mock.Of<IProjectFileWorkspace>(),
            Mock.Of<IWorkflowExecutionContextAccessor>(),
            Mock.Of<IHttpClientFactory>(),
            configuration,
            NullLogger<ProjectFileAgentTools>.Instance);

        ReactRemotionSandboxTools sandboxTools = new(
            Mock.Of<IHttpClientFactory>(),
            Mock.Of<IWorkflowExecutionContextAccessor>(),
            Mock.Of<IProjectFileWorkspace>(),
            Mock.Of<IAmazonS3>(),
            configuration,
            NullLogger<ReactRemotionSandboxTools>.Instance);

        WorkflowControlAgentTools workflowControlTools = new(
            Mock.Of<IWorkflowExecutionContextAccessor>(),
            NullLogger<WorkflowControlAgentTools>.Instance);

        RemotionSkillsService remotionSkillsService = new(
            Mock.Of<IHttpClientFactory>(),
            NullLogger<RemotionSkillsService>.Instance,
            configuration);

        RemotionSkillsAgentTools remotionSkillsTools = new(
            remotionSkillsService,
            NullLogger<RemotionSkillsAgentTools>.Instance);

        return new AgentToolProvider(projectFileTools, sandboxTools, workflowControlTools, remotionSkillsTools);
    }
}
