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

    /// <summary>
    /// When true, users must provide a free-text request (UserRequest) when executing this workflow.
    /// The request is passed as context to all agents throughout the run.
    /// When false, workflows execute without user input (UserRequest is null).
    /// </summary>
    public bool RequiresUserInput { get; set; } = false;

    public Project Project { get; set; } = null!;
    public ICollection<WorkflowStep> Steps { get; set; } = new List<WorkflowStep>();
    public ICollection<WorkflowExecution> Executions { get; set; } = new List<WorkflowExecution>();
}
