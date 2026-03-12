namespace ReelForge.Shared.Data.Models;

/// <summary>
/// A single step in a workflow definition, linked to an agent.
/// Enhanced with step type discriminator and conditional/loop support.
/// </summary>
public class WorkflowStep
{
    public Guid Id { get; set; }
    public Guid WorkflowDefinitionId { get; set; }
    public Guid AgentDefinitionId { get; set; }
    public int StepOrder { get; set; }
    public string? EdgeConditionJson { get; set; }
    public string? Label { get; set; }

    // Enhanced workflow support
    public StepType StepType { get; set; } = StepType.Agent;
    public string? ConditionExpression { get; set; }
    public string? LoopSourceExpression { get; set; }
    public int? LoopTargetStepOrder { get; set; }
    public int MaxIterations { get; set; } = 3;
    public int? MinScore { get; set; }
    public string? InputMappingJson { get; set; }
    public AgentInputContextMode? AgentInputContextMode { get; set; }
    public string? SelectedPriorStepOrdersJson { get; set; }
    public string? TrueBranchStepOrder { get; set; }
    public string? FalseBranchStepOrder { get; set; }

    /// <summary>
    /// JSON array of agent definition GUIDs to execute in parallel (StepType.Parallel only).
    /// All agents run concurrently; the primary AgentDefinitionId is included automatically.
    /// Output format: [{"agentName":"...","output":"..."},...]
    /// </summary>
    public string? ParallelAgentIdsJson { get; set; }

    public WorkflowDefinition WorkflowDefinition { get; set; } = null!;
    public AgentDefinition AgentDefinition { get; set; } = null!;
    public ICollection<WorkflowStepResult> Results { get; set; } = new List<WorkflowStepResult>();
}
