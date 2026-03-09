using ReelForge.Shared.Data.Models;

namespace ReelForge.WorkflowEngine.Execution;

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
    /// Free-text user request provided at execution time.
    /// Null when the workflow was executed without user input.
    /// </summary>
    public string? UserRequest { get; init; }

    /// <summary>
    /// Builds the final string input for an agent step by applying InputMappingJson field selection
    /// (if present) and appending the user request as a clearly separated context section.
    /// Expressions inside InputMappingJson are evaluated against <see cref="AccumulatedOutput"/>.
    /// </summary>
    public string BuildAgentInput()
    {
        string baseInput = string.IsNullOrEmpty(AccumulatedOutput)
            ? "Begin analysis of the project."
            : AccumulatedOutput;

        // Apply field mapping when the step specifies InputMappingJson.
        // This lets users compose a new JSON object by selecting fields from parallel or prior outputs.
        if (!string.IsNullOrWhiteSpace(Step.InputMappingJson))
        {
            string? mapped = ExpressionEvaluator.ApplyInputMapping(AccumulatedOutput, Step.InputMappingJson);
            if (mapped != null)
                baseInput = mapped;
        }

        if (string.IsNullOrWhiteSpace(UserRequest))
            return baseInput;

        return $"{baseInput}\n\n---\nUser Request:\n{UserRequest}";
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
