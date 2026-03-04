namespace ReelForge.Inference.Data.Models;

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

    public WorkflowExecution WorkflowExecution { get; set; } = null!;
    public WorkflowStep WorkflowStep { get; set; } = null!;
}
