using System;
using System.Collections.Generic;
using System.Threading;
using FluentAssertions;
using ReelForge.Shared.Data.Models;
using ReelForge.WorkflowEngine.Execution;
using ReelForge.WorkflowEngine.Execution.Context;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Covers <see cref="StepExecutionContext"/>'s wiring of <see cref="AgentInputBudget"/> — the
/// safety-contract-critical behaviors: budgeting is off by default (null <c>BudgetOptions</c>,
/// exactly like <see cref="StepExecutionContextTests"/> already exercises without ever setting
/// it), <c>PreviousStepOnly</c> is structurally exempt from budgeting no matter how small the
/// budget is, and <see cref="StepExecutionContext.LastBudgetSummary"/> only lights up when
/// something actually changed. <see cref="AgentInputBudgetTests"/>/<see cref="JsonOutputDigestTests"/>
/// cover the budgeting algorithm itself in isolation; this file only covers the wiring.
/// </summary>
public class StepExecutionContextBudgetTests
{
    [Fact]
    public void BuildAgentInput_with_null_BudgetOptions_matches_legacy_full_workflow_output()
    {
        StepExecutionContext context = CreateContext(
            step: CreateStep(agentType: AgentType.AuthorAgent, mode: AgentInputContextMode.FullWorkflow, stepOrder: 4),
            accumulatedOutput: "full-workflow",
            history: new List<StepOutputHistoryEntry>
            {
                new StepOutputHistoryEntry(1, "Analyze", "analysis"),
                new StepOutputHistoryEntry(2, "Animation", "animation-plan"),
                new StepOutputHistoryEntry(3, "Script", "script-output")
            },
            budgetOptions: null);

        string input = context.BuildAgentInput();

        // Exactly the pre-budget concatenation format — same expectations as
        // StepExecutionContextTests.BuildAgentInput_when_author_mode_unset_uses_full_workflow_default.
        string expected = "## Step 1: Analyze\nanalysis\n\n---\n\n## Step 2: Animation\nanimation-plan\n\n---\n\n## Step 3: Script\nscript-output";
        input.Should().Be(expected);
        context.LastBudgetSummary.Should().BeNull();
    }

    [Fact]
    public void BuildAgentInput_with_null_BudgetOptions_matches_legacy_previous_step_only_output()
    {
        StepExecutionContext context = CreateContext(
            step: CreateStep(agentType: AgentType.DependencyAnalyzer, mode: AgentInputContextMode.PreviousStepOnly, stepOrder: 3),
            accumulatedOutput: "irrelevant",
            history: new List<StepOutputHistoryEntry>
            {
                new StepOutputHistoryEntry(1, "Step 1", "first-output"),
                new StepOutputHistoryEntry(2, "Step 2", "second-output")
            },
            budgetOptions: null);

        context.BuildAgentInput().Should().Be("second-output");
        context.LastBudgetSummary.Should().BeNull();
    }

    [Fact]
    public void BuildAgentInput_with_null_BudgetOptions_matches_legacy_selected_prior_steps_output()
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
            },
            budgetOptions: null);

        string input = context.BuildAgentInput();

        input.Should().Be("## Step 1: Analyze\nanalysis\n\n---\n\n## Step 3: Inventory\ninventory");
        context.LastBudgetSummary.Should().BeNull();
    }

    [Fact]
    public void BuildAgentInput_with_null_BudgetOptions_matches_legacy_custom_mapped_subset_output()
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
            },
            budgetOptions: null);

        string input = context.BuildAgentInput();

        input.Should().Contain("\"picked\"");
        input.Should().Contain("\"bar\"");
        context.LastBudgetSummary.Should().BeNull();
    }

    [Fact]
    public void BuildAgentInput_previous_step_only_is_never_digested_even_with_a_tiny_budget()
    {
        string hugeStructuredOutput = "{\"view\":\"" + new string('v', 5000) + "\",\"meta\":{}}";
        AgentInputBudgetOptions tinyBudget = new()
        {
            Enabled = true,
            MaxInputChars = 10, // absurdly small — if PreviousStepOnly were budgeted, this would gut it
            MaxDigestedStepChars = 10,
            RecentStepsVerbatim = 0,
            DigestMaxStringChars = 5,
            DigestMaxArrayItems = 1,
            DigestMaxDepth = 1
        };
        StepExecutionContext context = CreateContext(
            step: CreateStep(agentType: AgentType.DependencyAnalyzer, mode: AgentInputContextMode.PreviousStepOnly, stepOrder: 2),
            accumulatedOutput: "irrelevant",
            history: new List<StepOutputHistoryEntry>
            {
                new StepOutputHistoryEntry(1, "Analyze", hugeStructuredOutput)
            },
            budgetOptions: tinyBudget);

        string input = context.BuildAgentInput();

        input.Should().Be(hugeStructuredOutput);
        context.LastBudgetSummary.Should().BeNull();
    }

    [Fact]
    public void BuildAgentInput_full_workflow_with_a_tiny_budget_shrinks_the_input()
    {
        string bigOutput = "{\"summary\":\"" + new string('x', 2000) + "\"}";
        AgentInputBudgetOptions tinyBudget = new()
        {
            Enabled = true,
            MaxInputChars = 500,
            MaxDigestedStepChars = 150,
            RecentStepsVerbatim = 1,
            DigestMaxStringChars = 40,
            DigestMaxArrayItems = 5,
            DigestMaxDepth = 6
        };
        StepExecutionContext context = CreateContext(
            step: CreateStep(agentType: AgentType.AuthorAgent, mode: AgentInputContextMode.FullWorkflow, stepOrder: 4),
            accumulatedOutput: "irrelevant",
            history: new List<StepOutputHistoryEntry>
            {
                new StepOutputHistoryEntry(1, "Old A", bigOutput),
                new StepOutputHistoryEntry(2, "Old B", bigOutput),
                new StepOutputHistoryEntry(3, "Recent", bigOutput)
            },
            budgetOptions: tinyBudget);

        // Unbudgeted (null options) full-workflow input, for comparison, built the same way
        // BuildAgentInput itself would.
        StepExecutionContext unbudgeted = CreateContext(
            step: CreateStep(agentType: AgentType.AuthorAgent, mode: AgentInputContextMode.FullWorkflow, stepOrder: 4),
            accumulatedOutput: "irrelevant",
            history: new List<StepOutputHistoryEntry>
            {
                new StepOutputHistoryEntry(1, "Old A", bigOutput),
                new StepOutputHistoryEntry(2, "Old B", bigOutput),
                new StepOutputHistoryEntry(3, "Recent", bigOutput)
            },
            budgetOptions: null);

        string budgetedInput = context.BuildAgentInput();
        string unbudgetedInput = unbudgeted.BuildAgentInput();

        budgetedInput.Length.Should().BeLessThan(unbudgetedInput.Length);
        budgetedInput.Should().Contain("## Step 3: Recent");
        budgetedInput.Should().Contain(bigOutput); // most recent step stays verbatim
        context.LastBudgetSummary.Should().NotBeNull();
        context.LastBudgetSummary.Should().Contain("context budget:");
        context.LastBudgetSummary.Should().Contain("steps digested");
    }

    [Fact]
    public void LastBudgetSummary_is_null_when_the_budget_does_not_change_anything()
    {
        AgentInputBudgetOptions generousBudget = new()
        {
            Enabled = true,
            MaxInputChars = 1_000_000, // far larger than the tiny history below
            MaxDigestedStepChars = 100,
            RecentStepsVerbatim = 1,
            DigestMaxStringChars = 50,
            DigestMaxArrayItems = 5,
            DigestMaxDepth = 6
        };
        StepExecutionContext context = CreateContext(
            step: CreateStep(agentType: AgentType.AuthorAgent, mode: AgentInputContextMode.FullWorkflow, stepOrder: 3),
            accumulatedOutput: "irrelevant",
            history: new List<StepOutputHistoryEntry>
            {
                new StepOutputHistoryEntry(1, "Analyze", "small-output"),
                new StepOutputHistoryEntry(2, "Dependency", "also-small")
            },
            budgetOptions: generousBudget);

        context.BuildAgentInput();

        context.LastBudgetSummary.Should().BeNull();
    }

    private static StepExecutionContext CreateContext(
        WorkflowStep step,
        string accumulatedOutput,
        IReadOnlyList<StepOutputHistoryEntry> history,
        AgentInputBudgetOptions? budgetOptions,
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
            CancellationToken = CancellationToken.None,
            BudgetOptions = budgetOptions
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
