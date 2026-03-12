namespace ReelForge.Shared.Data.Models;

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
    public string? OutputSchemaJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? Color { get; set; }
    public string? AvailableToolsJson { get; set; }
    public bool GeneratesOutput { get; set; }
    public string? OutputSchemaName { get; set; }

    /// <summary>How much prior workflow context this agent receives.</summary>
    public ContextMode ContextMode { get; set; } = ContextMode.LastStep;

    /// <summary>When ContextMode is LastN, specifies how many prior steps to include.</summary>
    public int? ContextWindowSize { get; set; }

    public ApplicationUser? Owner { get; set; }
    public ICollection<WorkflowStep> WorkflowSteps { get; set; } = new List<WorkflowStep>();
}
