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
}
