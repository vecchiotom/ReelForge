using System.Text.Json;
using ReelForge.Shared.Data.Models;

namespace ReelForge.WorkflowEngine.Execution;

public record StepOutputHistoryEntry(int StepOrder, string StepLabel, string Output);

/// <summary>
/// Context passed to each step executor containing all needed state.
/// </summary>
public class StepExecutionContext
{
    public required WorkflowExecution Execution { get; init; }
    public required WorkflowStep Step { get; init; }
    public required List<WorkflowStep> AllSteps { get; init; }
    public required string AccumulatedOutput { get; init; }
    public required int CurrentStepIndex { get; init; }
    public required int IterationCount { get; init; }
    public required string CorrelationId { get; init; }
    public CancellationToken CancellationToken { get; init; }

    /// <summary>
    /// Ordered history of completed step outputs so context modes can resolve inputs
    /// without querying persisted step results.
    /// </summary>
    public required IReadOnlyList<StepOutputHistoryEntry> StepOutputHistory { get; init; }

    /// <summary>
    /// Last concrete agent input built during this context lifetime.
    /// </summary>
    public string? LastResolvedAgentInput { get; private set; }

    /// <summary>
    /// Free-text user request provided at execution time.
    /// Null when the workflow was executed without user input.
    /// </summary>
    public string? UserRequest { get; init; }

    /// <summary>
    /// Builds the final string input for an agent step, applying the effective
    /// step-level mode and appending the optional user request.
    /// </summary>
    public string BuildAgentInput()
    {
        AgentInputContextMode effectiveMode = AgentInputContextResolver.ResolveEffectiveMode(Step);
        string baseInput = effectiveMode switch
        {
            AgentInputContextMode.FullWorkflow => BuildFullWorkflowInput(),
            AgentInputContextMode.PreviousStepOnly => BuildPreviousStepInput(),
            AgentInputContextMode.SelectedPriorSteps => BuildSelectedPriorStepsInput(),
            AgentInputContextMode.CustomMappedSubset => BuildCustomMappedSubsetInput(),
            _ => BuildFullWorkflowInput()
        };

        if (string.IsNullOrWhiteSpace(UserRequest))
        {
            LastResolvedAgentInput = baseInput;
            return baseInput;
        }

        LastResolvedAgentInput = $"{baseInput}\n\n---\nUser Request:\n{UserRequest}";
        return LastResolvedAgentInput;
    }

    private string BuildFullWorkflowInput()
    {
        return string.IsNullOrWhiteSpace(AccumulatedOutput)
            ? "Begin analysis of the project."
            : AccumulatedOutput;
    }

    private string BuildPreviousStepInput()
    {
        StepOutputHistoryEntry? latest = StepOutputHistory.LastOrDefault(h => !string.IsNullOrWhiteSpace(h.Output));
        return latest is null ? "Begin analysis of the project." : latest.Output;
    }

    private string BuildSelectedPriorStepsInput()
    {
        IReadOnlyList<int> selectedOrders = ParseSelectedStepOrders();
        if (selectedOrders.Count == 0)
            return "Begin analysis of the project.";

        HashSet<int> selectedSet = selectedOrders.ToHashSet();
        List<StepOutputHistoryEntry> selected = StepOutputHistory
            .Where(h => selectedSet.Contains(h.StepOrder) && !string.IsNullOrWhiteSpace(h.Output))
            .OrderBy(h => h.StepOrder)
            .ToList();

        return BuildConcatenated(selected);
    }

    private string BuildCustomMappedSubsetInput()
    {
        if (string.IsNullOrWhiteSpace(Step.InputMappingJson))
            return BuildFullWorkflowInput();

        string? mapped = ExpressionEvaluator.ApplyInputMapping(AccumulatedOutput, Step.InputMappingJson);
        if (string.IsNullOrWhiteSpace(mapped))
            return BuildFullWorkflowInput();

        return mapped;
    }

    private IReadOnlyList<int> ParseSelectedStepOrders()
    {
        if (string.IsNullOrWhiteSpace(Step.SelectedPriorStepOrdersJson))
            return [];

        try
        {
            List<int>? parsed = JsonSerializer.Deserialize<List<int>>(Step.SelectedPriorStepOrdersJson);
            if (parsed is null)
                return [];

            return parsed
                .Where(stepOrder => stepOrder > 0 && stepOrder < Step.StepOrder)
                .Distinct()
                .OrderBy(stepOrder => stepOrder)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string BuildConcatenated(IEnumerable<StepOutputHistoryEntry> history)
    {
        List<StepOutputHistoryEntry> parts = history.Where(h => !string.IsNullOrEmpty(h.Output)).ToList();
        if (parts.Count == 0)
            return "Begin analysis of the project.";
        if (parts.Count == 1)
            return parts[0].Output;

        return string.Join("\n\n---\n\n", parts.Select(p => $"## Step {p.StepOrder}: {p.StepLabel}\n{p.Output}"));
    }
}

/// <summary>
/// Result from a step execution determining what happens next.
/// </summary>
public class StepExecutionResult
{
    public required string Output { get; init; }
    public required int NextStepIndex { get; init; }
    public int NewIterationCount { get; init; }
    public int TokensUsed { get; init; }
    public long DurationMs { get; init; }
    public StepStatus Status { get; init; } = StepStatus.Completed;
    public string? ErrorDetails { get; init; }
    public int? IterationNumber { get; init; }

    /// <summary>
    /// S3/MinIO storage key for a video or image artifact produced during this step.
    /// Set by the RenderVideoAndUploadToStorage sandbox tool; keys now follow
    /// "projects/{projectId}/outputFiles/{executionId}/..." rather than the old
    /// "outputs/" prefix.
    /// </summary>
    public string? OutputStorageKey { get; init; }
}
