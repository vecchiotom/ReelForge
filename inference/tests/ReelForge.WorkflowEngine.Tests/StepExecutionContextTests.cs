using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Threading;
using ReelForge.Shared.Data.Models;
using ReelForge.WorkflowEngine.Execution;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

public class StepExecutionContextTests
{
    [Fact]
    public void BuildAgentInput_when_author_mode_unset_uses_full_workflow_default()
    {
        StepExecutionContext context = CreateContext(
            step: CreateStep(agentType: AgentType.AuthorAgent, mode: null, stepOrder: 4),
            accumulatedOutput: "full-workflow",
            history: new List<StepOutputHistoryEntry>
            {
                new StepOutputHistoryEntry(1, "Analyze", "analysis"),
                new StepOutputHistoryEntry(2, "Animation", "animation-plan"),
                new StepOutputHistoryEntry(3, "Script", "script-output")
            });

        string input = context.BuildAgentInput();

        input.Should().Contain("## Step 1: Analyze");
        input.Should().Contain("analysis");
        input.Should().Contain("## Step 2: Animation");
        input.Should().Contain("animation-plan");
        input.Should().Contain("## Step 3: Script");
        input.Should().Contain("script-output");
    }

    [Fact]
    public void BuildAgentInput_when_mode_unset_uses_agent_type_default()
    {
        StepExecutionContext context = CreateContext(
            step: CreateStep(agentType: AgentType.DependencyAnalyzer, mode: null, stepOrder: 3),
            accumulatedOutput: "full-workflow",
            history: new List<StepOutputHistoryEntry>
            {
                new StepOutputHistoryEntry(1, "Step 1", "first-output"),
                new StepOutputHistoryEntry(2, "Step 2", "second-output")
            });

        string input = context.BuildAgentInput();

        input.Should().Be("second-output");
    }

    /// <summary>
    /// Bug group A.1 regression: PreviousStepOnly must resolve to the entry with the GREATEST
    /// StepOrder strictly less than this step's own StepOrder — not "the most recently appended
    /// entry" (LastOrDefault over history in append order). Those two only coincide when history
    /// is append-only; after a ReviewLoop loop-back re-executes an earlier step, a later,
    /// higher-StepOrder entry (here the ReviewLoop step's own StepOrder-4 verdict from the PRIOR
    /// iteration) can be appended BEFORE a lower-StepOrder entry that is actually "the step
    /// immediately before me" (StepOrder 2's fresh re-run). Without the fix, the re-entered
    /// StepOrder-3 step would be fed the review's own JSON output instead of step 2's.
    /// </summary>
    [Fact]
    public void BuildAgentInput_previous_step_only_picks_the_greatest_lesser_StepOrder_not_the_last_appended_entry()
    {
        StepExecutionContext context = CreateContext(
            step: CreateStep(agentType: AgentType.DependencyAnalyzer, mode: null, stepOrder: 3),
            accumulatedOutput: "irrelevant",
            history: new List<StepOutputHistoryEntry>
            {
                // A stale higher-StepOrder entry (from a PRIOR loop iteration) appended AFTER the
                // real "previous step" entry, but which belongs to a step order >= this step's own
                // — must never be picked as "the previous step" no matter its append position.
                new StepOutputHistoryEntry(1, "Analyze", "first-output"),
                new StepOutputHistoryEntry(4, "Review (stale, prior iteration)", "{\"score\":3}"),
                new StepOutputHistoryEntry(2, "StoryEditor", "second-output")
            });

        string input = context.BuildAgentInput();

        input.Should().Be("second-output");
    }

    [Fact]
    public void BuildAgentInput_selected_prior_steps_includes_only_selected_in_order()
    {
        StepExecutionContext context = CreateContext(
            step: CreateStep(
                agentType: AgentType.Custom,
                mode: AgentInputContextMode.SelectedPriorSteps,
                stepOrder: 5,
                selectedPriorStepOrdersJson: "[3,1]"),
            accumulatedOutput: "full-workflow",
            history: new List<StepOutputHistoryEntry>
            {
                new StepOutputHistoryEntry(1, "Analyze", "analysis"),
                new StepOutputHistoryEntry(2, "Dependency", "deps"),
                new StepOutputHistoryEntry(3, "Inventory", "inventory")
            });

        string input = context.BuildAgentInput();

        input.Should().Contain("## Step 1: Analyze");
        input.Should().Contain("analysis");
        input.Should().Contain("## Step 3: Inventory");
        input.Should().Contain("inventory");
        input.Should().NotContain("Dependency");
    }

    [Fact]
    public void BuildAgentInput_custom_mapped_subset_uses_input_mapping_json()
    {
        StepExecutionContext context = CreateContext(
            step: CreateStep(
                agentType: AgentType.Custom,
                mode: AgentInputContextMode.CustomMappedSubset,
                stepOrder: 2,
                inputMappingJson: "{\"picked\":\"$.foo\"}"),
            accumulatedOutput: "{\"foo\":\"bar\",\"other\":123}",
            history: new List<StepOutputHistoryEntry>
            {
                new StepOutputHistoryEntry(1, "Analyze", "analysis")
            });

        string input = context.BuildAgentInput();

        input.Should().Contain("\"picked\"");
        input.Should().Contain("\"bar\"");
    }

    [Fact]
    public void BuildAgentInput_appends_user_request_for_all_modes()
    {
        StepExecutionContext context = CreateContext(
            step: CreateStep(agentType: AgentType.DirectorAgent, mode: AgentInputContextMode.FullWorkflow, stepOrder: 2),
            accumulatedOutput: "workflow-output",
            history: new List<StepOutputHistoryEntry>
            {
                new StepOutputHistoryEntry(1, "Analyze", "analysis")
            },
            userRequest: "Please prioritize transitions");

        string input = context.BuildAgentInput();

        input.Should().Contain("analysis");
        input.Should().Contain("User Request:");
        input.Should().Contain("Please prioritize transitions");
        context.LastResolvedAgentInput.Should().Be(input);
    }

    private static StepExecutionContext CreateContext(
        WorkflowStep step,
        string accumulatedOutput,
        IReadOnlyList<StepOutputHistoryEntry> history,
        string userRequest = null)
    {
        return new StepExecutionContext
        {
            Execution = new WorkflowExecution(),
            Step = step,
            AllSteps = new List<WorkflowStep> { step },
            AccumulatedOutput = accumulatedOutput,
            StepOutputHistory = history,
            CurrentStepIndex = 0,
            IterationCount = 0,
            CorrelationId = "test",
            UserRequest = userRequest,
            CancellationToken = CancellationToken.None
        };
    }

    private static WorkflowStep CreateStep(
        AgentType agentType,
        AgentInputContextMode? mode,
        int stepOrder,
        string selectedPriorStepOrdersJson = null,
        string inputMappingJson = null)
    {
        return new WorkflowStep
        {
            Id = Guid.NewGuid(),
            WorkflowDefinitionId = Guid.NewGuid(),
            AgentDefinitionId = Guid.NewGuid(),
            StepOrder = stepOrder,
            StepType = StepType.Agent,
            AgentInputContextMode = mode,
            SelectedPriorStepOrdersJson = selectedPriorStepOrdersJson,
            InputMappingJson = inputMappingJson,
            AgentDefinition = new AgentDefinition
            {
                Id = Guid.NewGuid(),
                Name = agentType.ToString(),
                AgentType = agentType,
                Description = "",
                SystemPrompt = "",
                IsBuiltIn = true
            },
            WorkflowDefinition = new WorkflowDefinition
            {
                Id = Guid.NewGuid(),
                Name = "test",
                ProjectId = Guid.NewGuid()
            }
        };
    }
}
