namespace ReelForge.Inference.Data.Models;

/// <summary>
/// A quality review score for a workflow execution iteration.
/// </summary>
public class ReviewScore
{
    public Guid Id { get; set; }
    public Guid WorkflowExecutionId { get; set; }
    public int IterationNumber { get; set; }
    public int Score { get; set; }
    public string Comments { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public WorkflowExecution WorkflowExecution { get; set; } = null!;
}
