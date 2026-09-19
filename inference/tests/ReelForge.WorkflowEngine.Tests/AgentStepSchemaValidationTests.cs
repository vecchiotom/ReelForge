using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Data.OutputSchemas;
using ReelForge.WorkflowEngine.Agents;
using ReelForge.WorkflowEngine.Agents.Tools;
using ReelForge.WorkflowEngine.Execution;
using ReelForge.WorkflowEngine.Execution.StepExecutors;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// An agent that DECLARES a structured output schema must actually produce a parseable JSON
/// object, and the step that produced it is where that has to be caught.
///
/// <para>
/// Regression test for a failure seen live while producing a real promo video: a
/// <c>VideoStoryEditor</c> step returned 38KB of pure reasoning prose containing not a single
/// brace, was recorded <c>Completed</c>, and the workflow went on to spend roughly fifty more
/// minutes running MotionGraphicsPlanner, SoundDesigner and Colorist on top of a decision that did
/// not exist — until <c>VideoCompile</c> finally failed with "Decision input did not contain a
/// recognizable JSON object", a message naming the compile step rather than the step that actually
/// broke. The schema contract was declared (<c>ChatResponseFormat.ForJsonSchema</c>) but never
/// verified.
/// </para>
/// </summary>
public class AgentStepSchemaValidationTests
{
    [Fact]
    public async Task A_schema_declaring_agent_that_returns_only_prose_fails_its_own_step()
    {
        StepExecutionResult result = await RunAgentReturning(
            "I'll analyse the available context before deciding. The four shots map to four "
            + "source clips. Plan: open on the hero and close on it. No JSON here at all.",
            outputSchemaType: typeof(VideoEditDecisionOutput));

        result.Status.Should().Be(StepStatus.Failed);
        // The message must name the agent and its schema, so the failure points at the step that
        // actually broke instead of at a distant consumer.
        result.ErrorDetails.Should().Contain("VideoStoryEditor");
        result.ErrorDetails.Should().Contain(nameof(VideoEditDecisionOutput));
    }

    [Fact]
    public async Task A_schema_declaring_agent_passes_when_its_answer_follows_reasoning_prose()
    {
        // The common, legitimate shape for a reasoning model: extensive prose, then the real
        // answer last. Validation uses the same last-object extractor the consumers use, so this
        // must not be mistaken for the prose-only failure above.
        StepExecutionResult result = await RunAgentReturning(
            "Let me think. Candidate spans {s0,s0} and {s1,s1} look best.\n"
            + "{\"keep\":[{\"shotId\":\"s0\"}],\"editRationale\":\"opens on the hero\"}",
            outputSchemaType: typeof(VideoEditDecisionOutput));

        result.Status.Should().Be(StepStatus.Completed);
    }

    [Fact]
    public async Task An_agent_with_no_declared_schema_is_not_required_to_emit_json()
    {
        // Prose-only agents are a normal, supported case — the check must key off the declared
        // schema, not apply to every agent.
        StepExecutionResult result = await RunAgentReturning(
            "A plain prose summary with no JSON whatsoever.",
            outputSchemaType: null);

        result.Status.Should().Be(StepStatus.Completed);
    }

    [Fact]
    public async Task An_agent_that_called_FailWorkflow_actually_aborts_its_step()
    {
        // FailWorkflow throws AgentWorkflowException, but FunctionInvokingChatClient catches a
        // tool's exception and hands it back to the model as a tool result, so the abort never
        // propagates on its own. Seen live: a Colorist step looped read_project_file ->
        // fail_workflow for over forty minutes without ever failing, while the tool's own
        // documentation promised it would abort the workflow immediately. The executor must honor
        // the recorded abort reason after the run.
        var accessor = new WorkflowExecutionContextAccessor();
        var agent = new StubAgent("...reasoning, no decision...", typeof(VideoEditDecisionOutput));

        var registry = new Mock<IAgentRegistry>();
        registry.Setup(r => r.GetByType(It.IsAny<AgentType>(), It.IsAny<Guid?>())).Returns(agent);

        var tools = new WorkflowControlAgentTools(
            accessor, NullLogger<WorkflowControlAgentTools>.Instance);

        var executor = new AgentStepExecutor(
            registry.Object, accessor,
            Mock.Of<IMotionGraphicsPlacementAnnotator>(),
            NullLogger<AgentStepExecutor>.Instance);

        // Stand in for the model calling the tool mid-run and the tool layer swallowing the throw.
        agent.OnRun = () =>
        {
            try { tools.FailWorkflow("analysis view unusable").GetAwaiter().GetResult(); }
            catch (AgentWorkflowException) { /* swallowed, exactly as the tool layer does */ }
        };

        var step = new WorkflowStep
        {
            StepOrder = 4,
            StepType = StepType.Agent,
            AgentDefinition = new AgentDefinition { Name = "Colorist", AgentType = AgentType.Colorist }
        };
        var context = new StepExecutionContext
        {
            Execution = new WorkflowExecution { Id = Guid.NewGuid(), ProjectId = Guid.NewGuid() },
            Step = step,
            AllSteps = new List<WorkflowStep> { step },
            AccumulatedOutput = string.Empty,
            StepOutputHistory = new List<StepOutputHistoryEntry>(),
            CurrentStepIndex = 0,
            IterationCount = 0,
            CorrelationId = "test",
            CancellationToken = CancellationToken.None
        };

        AgentWorkflowException thrown = await Assert.ThrowsAsync<AgentWorkflowException>(
            () => executor.ExecuteAsync(context));

        thrown.Reason.Should().Be("analysis view unusable");
    }

    private static async Task<StepExecutionResult> RunAgentReturning(string output, Type? outputSchemaType)
    {
        var agent = new StubAgent(output, outputSchemaType);

        var registry = new Mock<IAgentRegistry>();
        registry.Setup(r => r.GetByType(It.IsAny<AgentType>(), It.IsAny<Guid?>())).Returns(agent);

        var executor = new AgentStepExecutor(
            registry.Object,
            new WorkflowExecutionContextAccessor(),
            Mock.Of<IMotionGraphicsPlacementAnnotator>(),
            NullLogger<AgentStepExecutor>.Instance);

        var step = new WorkflowStep
        {
            StepOrder = 2,
            StepType = StepType.Agent,
            AgentDefinition = new AgentDefinition
            {
                Name = "VideoStoryEditor",
                AgentType = AgentType.VideoStoryEditor
            }
        };

        var context = new StepExecutionContext
        {
            Execution = new WorkflowExecution { Id = Guid.NewGuid(), ProjectId = Guid.NewGuid() },
            Step = step,
            AllSteps = new List<WorkflowStep> { step },
            AccumulatedOutput = string.Empty,
            StepOutputHistory = new List<StepOutputHistoryEntry>(),
            CurrentStepIndex = 0,
            IterationCount = 0,
            CorrelationId = "test",
            CancellationToken = CancellationToken.None
        };

        return await executor.ExecuteAsync(context);
    }

    private sealed class StubAgent : IReelForgeAgent
    {
        private readonly string _output;

        public StubAgent(string output, Type? outputSchemaType)
        {
            _output = output;
            OutputSchemaType = outputSchemaType;
        }

        public Guid? AgentId => null;
        public string Name => "VideoStoryEditor";
        public string Description => "stub";
        public string SystemPrompt => "stub";
        public AgentType AgentType => AgentType.VideoStoryEditor;
        public IReadOnlyList<AIFunction> Tools => Array.Empty<AIFunction>();
        public string? OutputSchemaJson => null;
        public Type? OutputSchemaType { get; }

        public Action? OnRun { get; set; }

        public Task<AgentRunResult> RunAsync(string prompt, Guid? agentDefinitionId = null, CancellationToken ct = default)
        {
            OnRun?.Invoke();
            return Task.FromResult(new AgentRunResult { Output = _output, TokensUsed = 1 });
        }
    }
}
