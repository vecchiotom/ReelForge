using ReelForge.Shared.Data.Models;

namespace ReelForge.WorkflowEngine.Execution;

/// <summary>
/// Strategy interface for executing different types of workflow steps.
/// </summary>
public interface IStepExecutor
{
    StepType StepType { get; }
    Task<StepExecutionResult> ExecuteAsync(StepExecutionContext context);
}
