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

        input.Should().Contain("workflow-output");
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
