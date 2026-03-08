namespace ReelForge.Shared.IntegrationEvents;

/// <summary>
/// Published by Inference API when user triggers workflow execution.
/// </summary>
public record WorkflowExecutionRequested
{
    public Guid ExecutionId { get; init; }
    public Guid WorkflowDefinitionId { get; init; }
    public Guid ProjectId { get; init; }
    public Guid InitiatedByUserId { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public DateTime RequestedAt { get; init; } = DateTime.UtcNow;
    /// <summary>
    /// Optional free-text user request passed as context to all agents.
    /// Null when the workflow was executed without user input.
    /// </summary>
    public string? UserRequest { get; init; }
}

/// <summary>
/// Published by WorkflowEngine on successful completion.
/// </summary>
public record WorkflowExecutionCompleted
{
    public Guid ExecutionId { get; init; }
    public Guid ProjectId { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public string FinalStatus { get; init; } = string.Empty;
    public string? ResultJson { get; init; }
    public DateTime CompletedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Event produced when a user (or admin) requests that a running or queued
/// workflow execution be aborted. The WorkflowEngine will handle cancellation
/// and update the execution status accordingly.
/// </summary>
public record WorkflowExecutionStopRequested
{
    public Guid ExecutionId { get; init; }
    public Guid RequestedByUserId { get; init; }
    public DateTime RequestedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Published per step for real-time tracking.
/// </summary>
public record WorkflowStepCompleted
{
    public Guid ExecutionId { get; init; }
    public Guid StepId { get; init; }
    public Guid StepResultId { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public string StepStatus { get; init; } = string.Empty;
    public int TokensUsed { get; init; }
    public long DurationMs { get; init; }
    public DateTime CompletedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Published on unrecoverable failure.
/// </summary>
public record WorkflowExecutionFailed
{
    public Guid ExecutionId { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
    public DateTime FailedAt { get; init; } = DateTime.UtcNow;
}
