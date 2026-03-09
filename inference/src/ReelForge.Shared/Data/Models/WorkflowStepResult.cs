namespace ReelForge.Shared.Data.Models;

/// <summary>
/// The result of executing a single workflow step.
/// </summary>
public class WorkflowStepResult
{
    public Guid Id { get; set; }
    public Guid WorkflowExecutionId { get; set; }
    public Guid WorkflowStepId { get; set; }
    public string Output { get; set; } = string.Empty;
    public int TokensUsed { get; set; }
    public long DurationMs { get; set; }
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;

    // Enhanced fields
    public string? InputJson { get; set; }
    public string? OutputJson { get; set; }
    public StepStatus Status { get; set; } = StepStatus.Pending;
    public string? ErrorDetails { get; set; }
    public int? IterationNumber { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// S3/MinIO storage key for a video or image artifact produced by this step.
    /// Null when the step produces no media output.
    /// Key format: projects/{projectId}/outputFiles/{executionId}/{fileName}
    /// </summary>
    public string? OutputStorageKey { get; set; }

    public WorkflowExecution WorkflowExecution { get; set; } = null!;
    public WorkflowStep WorkflowStep { get; set; } = null!;
}
