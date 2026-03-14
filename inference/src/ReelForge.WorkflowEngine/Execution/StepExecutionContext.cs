using System.Text.Json;
using ReelForge.Shared.Data.Models;

namespace ReelForge.WorkflowEngine.Execution;

public record StepOutputHistoryEntry(int StepOrder, string StepLabel, string Output);

/// <summary>
/// Context passed to each step executor containing all needed state.
/// </summary>
public class StepExecutionContext
{
    private readonly List<string> _retryFeedback = new();

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
    /// Stores retry feedback (for example schema validation errors) so the next
    /// agent attempt can self-correct based on the previous failure.
    /// </summary>
    public IReadOnlyList<string> RetryFeedback => _retryFeedback.AsReadOnly();

    /// <summary>
    /// Prompt-ready retry guidance text built from previous failed attempts.
    /// Empty when no retry feedback has been recorded.
    /// </summary>
    public string RetryGuidance => BuildRetryGuidance();

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

        string composedInput = baseInput;

        if (!string.IsNullOrWhiteSpace(UserRequest))
            composedInput = $"{composedInput}\n\n---\nUser Request:\n{UserRequest}";

        string retryGuidance = BuildRetryGuidance();
        if (!string.IsNullOrWhiteSpace(retryGuidance))
            composedInput = $"{composedInput}\n\n---\nRetry Guidance:\n{retryGuidance}";

        LastResolvedAgentInput = composedInput;
        return composedInput;
    }

    public void RecordRetryFeedback(int attemptNumber, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return;

        string normalized = reason.Trim();
        if (normalized.Length > 2000)
            normalized = normalized[..2000];

        _retryFeedback.Add($"Attempt {attemptNumber}: {normalized}");

        // Keep only the latest few feedback entries to avoid runaway prompt growth.
        while (_retryFeedback.Count > 3)
            _retryFeedback.RemoveAt(0);
    }

    private string BuildFullWorkflowInput()
    {
        List<StepOutputHistoryEntry> history = StepOutputHistory
            .Where(h => h.StepOrder < Step.StepOrder && !string.IsNullOrWhiteSpace(h.Output))
            .OrderBy(h => h.StepOrder)
            .ToList();

        return BuildConcatenated(history);
    }

    private string BuildPreviousStepInput()
    {
        StepOutputHistoryEntry? latest = StepOutputHistory.LastOrDefault(h => !string.IsNullOrWhiteSpace(h.Output));
        return latest is null ? "[\"Begin analysis of the project.\"]" : latest.Output;
    }

    private string BuildSelectedPriorStepsInput()
    {
        IReadOnlyList<int> selectedOrders = ParseSelectedStepOrders();
        if (selectedOrders.Count == 0)
            return "[\"Begin analysis of the project.\"]";

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
            return "[\"Begin analysis of the project.\"]";
        if (parts.Count == 1)
            return parts[0].Output;

        return string.Join("\n\n---\n\n", parts.Select(p => $"## Step {p.StepOrder}: {p.StepLabel}\n{p.Output}"));
    }

    private string BuildRetryGuidance()
    {
        if (_retryFeedback.Count == 0)
            return string.Empty;

        return string.Join(
            "\n",
            _retryFeedback.Select(message => $"- {message}"))
            + "\nReturn output that strictly matches the expected JSON schema and is valid JSON.";
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
