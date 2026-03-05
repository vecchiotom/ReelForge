namespace ReelForge.Shared.Data.Models;

/// <summary>
/// A user-defined or built-in workflow composed of ordered steps.
/// </summary>
public class WorkflowDefinition
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid ProjectId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Project Project { get; set; } = null!;
    public ICollection<WorkflowStep> Steps { get; set; } = new List<WorkflowStep>();
    public ICollection<WorkflowExecution> Executions { get; set; } = new List<WorkflowExecution>();
}
