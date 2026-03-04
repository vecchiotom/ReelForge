namespace ReelForge.Inference.Data.Models;

/// <summary>
/// Defines an agent's configuration, either built-in or user-created.
/// </summary>
public class AgentDefinition
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public AgentType AgentType { get; set; }
    public bool IsBuiltIn { get; set; }
    public Guid? OwnerId { get; set; }
    public string? ConfigJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ApplicationUser? Owner { get; set; }
    public ICollection<WorkflowStep> WorkflowSteps { get; set; } = new List<WorkflowStep>();
}
