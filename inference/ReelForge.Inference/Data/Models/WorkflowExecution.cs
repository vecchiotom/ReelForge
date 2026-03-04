namespace ReelForge.Inference.Data.Models;

/// <summary>
/// Represents a single run of a workflow.
/// </summary>
public class WorkflowExecution
{
    public Guid Id { get; set; }
    public Guid WorkflowDefinitionId { get; set; }
    public Guid ProjectId { get; set; }
    public ExecutionStatus Status { get; set; } = ExecutionStatus.Queued;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Guid? CurrentStepId { get; set; }
    public int IterationCount { get; set; }
    public string? ResultJson { get; set; }

    public WorkflowDefinition WorkflowDefinition { get; set; } = null!;
    public Project Project { get; set; } = null!;
    public WorkflowStep? CurrentStep { get; set; }
    public ICollection<WorkflowStepResult> StepResults { get; set; } = new List<WorkflowStepResult>();
    public ICollection<ReviewScore> ReviewScores { get; set; } = new List<ReviewScore>();
}
