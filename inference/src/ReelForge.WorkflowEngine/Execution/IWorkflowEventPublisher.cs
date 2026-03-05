using ReelForge.Shared.Data.Models;

namespace ReelForge.WorkflowEngine.Execution;

public interface IWorkflowEventPublisher
{
    Task PublishStepCompletedAsync(
        WorkflowExecution execution,
        WorkflowStep step,
        WorkflowStepResult stepResult,
        CancellationToken ct);

    Task PublishExecutionCompletedAsync(WorkflowExecution execution, CancellationToken ct);
    Task PublishExecutionFailedAsync(WorkflowExecution execution, CancellationToken ct);
}
