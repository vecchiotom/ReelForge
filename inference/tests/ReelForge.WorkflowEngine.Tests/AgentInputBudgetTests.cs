using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using ReelForge.WorkflowEngine.Execution.Context;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Covers <see cref="AgentInputBudget"/> directly. The under-budget/single-part cases build the
/// expected "legacy" concatenation BY HAND rather than by calling any production helper — those
/// are the regression guard that a budgeted call and an unbudgeted call produce the exact same
/// string whenever nothing actually needs shrinking (the single most important behavior of this
/// feature, per its safety contract).
/// </summary>
public class AgentInputBudgetTests
{
    private static AgentInputBudgetOptions DefaultOptions() => new()
    {
        Enabled = true,
        MaxInputChars = 1000,
        MaxDigestedStepChars = 200,
        RecentStepsVerbatim = 1,
        DigestMaxStringChars = 50,
        DigestMaxArrayItems = 5,
        DigestMaxDepth = 6
    };

    private static string LegacyFormat(IReadOnlyList<(int StepOrder, string StepLabel, string Output)> parts)
    {
        if (parts.Count == 0)
            return "[\"Begin analysis of the project.\"]";
        if (parts.Count == 1)
            return parts[0].Output;

        return string.Join("\n\n---\n\n", parts.Select(p => $"## Step {p.StepOrder}: {p.StepLabel}\n{p.Output}"));
    }

    [Fact]
    public void Apply_under_budget_is_byte_identical_to_the_legacy_format()
    {
        List<(int StepOrder, string StepLabel, string Output)> parts = new()
        {
            (1, "Analyze", "analysis-output"),
            (2, "Dependency", "dependency-output"),
            (3, "Inventory", "inventory-output")
        };
        AgentInputBudgetOptions options = DefaultOptions(); // MaxInputChars = 1000, far above these tiny parts

        BudgetedConcatenation result = AgentInputBudget.Apply(parts, options);

        result.Text.Should().Be(LegacyFormat(parts));
        result.OriginalChars.Should().Be(result.Text.Length);
        result.FinalChars.Should().Be(result.Text.Length);
        result.DigestedStepCount.Should().Be(0);
        result.DroppedStepCount.Should().Be(0);
    }

    [Fact]
    public void Apply_single_part_returns_its_output_verbatim_with_no_header()
    {
        List<(int StepOrder, string StepLabel, string Output)> parts = new()
        {
            (1, "Analyze", "solo-output")
        };

        BudgetedConcatenation result = AgentInputBudget.Apply(parts, DefaultOptions());

        result.Text.Should().Be("solo-output");
        result.Text.Should().Be(LegacyFormat(parts));
    }

    [Fact]
    public void Apply_empty_parts_returns_the_placeholder()
    {
        BudgetedConcatenation result = AgentInputBudget.Apply(
            new List<(int StepOrder, string StepLabel, string Output)>(), DefaultOptions());

        result.Text.Should().Be("[\"Begin analysis of the project.\"]");
        result.DigestedStepCount.Should().Be(0);
        result.DroppedStepCount.Should().Be(0);
    }

    [Fact]
    public void Apply_disabled_is_a_no_op_even_when_hugely_over_budget()
    {
        List<(int StepOrder, string StepLabel, string Output)> parts = new()
        {
            (1, "Analyze", new string('a', 5000)),
            (2, "Dependency", new string('b', 5000))
        };
        AgentInputBudgetOptions options = DefaultOptions();
        options.Enabled = false;
        options.MaxInputChars = 100; // would be hugely over budget if enabled

        BudgetedConcatenation result = AgentInputBudget.Apply(parts, options);

        result.Text.Should().Be(LegacyFormat(parts));
        result.DigestedStepCount.Should().Be(0);
        result.DroppedStepCount.Should().Be(0);
    }

    [Fact]
    public void Apply_negative_or_zero_MaxInputChars_disables_the_cap()
    {
        List<(int StepOrder, string StepLabel, string Output)> parts = new()
        {
            (1, "Analyze", new string('a', 5000))
        };
        AgentInputBudgetOptions options = DefaultOptions();
        options.MaxInputChars = 0;

        BudgetedConcatenation result = AgentInputBudget.Apply(parts, options);

        result.Text.Should().Be(LegacyFormat(parts));
        result.DigestedStepCount.Should().Be(0);
    }

    [Fact]
    public void Apply_digests_older_steps_once_over_budget_but_keeps_the_most_recent_verbatim()
    {
        string bigJson = "{\"summary\":\"" + new string('x', 500) + "\"}";
        List<(int StepOrder, string StepLabel, string Output)> parts = new()
        {
            (1, "Old A", bigJson),
            (2, "Old B", bigJson),
            (3, "Recent", bigJson) // RecentStepsVerbatim = 1, so only this one is protected
        };
        AgentInputBudgetOptions options = DefaultOptions();
        options.MaxInputChars = 700; // plain concatenation of 3 big parts is well over this

        BudgetedConcatenation result = AgentInputBudget.Apply(parts, options);

        result.DigestedStepCount.Should().Be(2); // steps 1 and 2
        result.Text.Should().Contain(bigJson); // step 3's untouched body is still present verbatim
        result.Text.Should().Contain("## Step 3: Recent");
        result.FinalChars.Should().BeLessThan(result.OriginalChars);
    }

    [Fact]
    public void Apply_recent_N_steps_are_never_digested_even_though_they_are_JSON()
    {
        string bigJson = "{\"summary\":\"" + new string('x', 500) + "\"}";
        List<(int StepOrder, string StepLabel, string Output)> parts = new()
        {
            (1, "Old", bigJson),
            (2, "Recent A", bigJson),
            (3, "Recent B", bigJson)
        };
        AgentInputBudgetOptions options = DefaultOptions();
        options.RecentStepsVerbatim = 2; // steps 2 and 3 protected
        options.MaxInputChars = 700;

        BudgetedConcatenation result = AgentInputBudget.Apply(parts, options);

        result.Text.Should().Contain($"## Step 2: Recent A\n{bigJson}");
        result.Text.Should().Contain($"## Step 3: Recent B\n{bigJson}");
        result.DigestedStepCount.Should().Be(1); // only step 1
    }

    [Fact]
    public void Apply_drops_oldest_first_with_a_marker_once_digesting_alone_is_not_enough()
    {
        // Each part's digested form is still ~200 chars (DigestMaxDepth JSON structural digest
        // caps strings but the overall shape survives), so three digested parts plus one
        // protected part can still exceed a very small MaxInputChars — forcing the drop phase.
        string bigJson = "{\"summary\":\"" + new string('x', 5000) + "\"}";
        List<(int StepOrder, string StepLabel, string Output)> parts = new()
        {
            (1, "Oldest", bigJson),
            (2, "Middle", bigJson),
            (3, "Recent", bigJson)
        };
        AgentInputBudgetOptions options = DefaultOptions();
        options.RecentStepsVerbatim = 1;
        options.MaxDigestedStepChars = 300;
        options.MaxInputChars = 400; // small enough that even two digested + one protected won't fit

        BudgetedConcatenation result = AgentInputBudget.Apply(parts, options);

        result.DroppedStepCount.Should().BeGreaterThan(0);
        result.Text.Should().Contain("## Step 1: Oldest");
        result.Text.Should().Contain("Step 1 output omitted by context budget");
        result.Text.Should().Contain($"{bigJson.Length} chars");
        // Step 2 (also non-protected) should be dropped before step 3 (protected) is ever touched,
        // since drops proceed oldest-first.
        result.Text.Should().Contain("## Step 3: Recent");
        result.Text.Should().Contain(bigJson); // the protected step 3 body, still fully intact
    }

    [Fact]
    public void Apply_protected_only_overflow_returns_protected_parts_intact_and_reports_the_overflow()
    {
        string hugeJson = "{\"summary\":\"" + new string('x', 5000) + "\"}";
        List<(int StepOrder, string StepLabel, string Output)> parts = new()
        {
            (1, "Recent A", hugeJson),
            (2, "Recent B", hugeJson)
        };
        AgentInputBudgetOptions options = DefaultOptions();
        options.RecentStepsVerbatim = 2; // both parts protected — nothing left to digest or drop
        options.MaxInputChars = 200; // impossible to hit with two ~5000-char protected parts

        BudgetedConcatenation result = AgentInputBudget.Apply(parts, options);

        result.Text.Should().Contain(hugeJson);
        result.DigestedStepCount.Should().Be(0);
        result.DroppedStepCount.Should().Be(0);
        result.FinalChars.Should().BeGreaterThan(options.MaxInputChars); // still over budget — never corrupted
    }

    [Fact]
    public void Apply_stats_reflect_original_and_final_character_counts()
    {
        string bigJson = "{\"summary\":\"" + new string('x', 2000) + "\"}";
        List<(int StepOrder, string StepLabel, string Output)> parts = new()
        {
            (1, "Old", bigJson),
            (2, "Recent", bigJson)
        };
        AgentInputBudgetOptions options = DefaultOptions();
        options.RecentStepsVerbatim = 1;
        options.MaxInputChars = 1500;

        BudgetedConcatenation result = AgentInputBudget.Apply(parts, options);

        result.OriginalChars.Should().Be(LegacyFormat(parts).Length);
        result.FinalChars.Should().Be(result.Text.Length);
        result.FinalChars.Should().BeLessThan(result.OriginalChars);
    }
}
