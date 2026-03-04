namespace ReelForge.Inference.Data.Models;

/// <summary>
/// A single step in a workflow definition, linked to an agent.
/// </summary>
public class WorkflowStep
{
    public Guid Id { get; set; }
    public Guid WorkflowDefinitionId { get; set; }
    public Guid AgentDefinitionId { get; set; }
    public int StepOrder { get; set; }
    public string? EdgeConditionJson { get; set; }
    public string? Label { get; set; }

    public WorkflowDefinition WorkflowDefinition { get; set; } = null!;
    public AgentDefinition AgentDefinition { get; set; } = null!;
    public ICollection<WorkflowStepResult> Results { get; set; } = new List<WorkflowStepResult>();
}
