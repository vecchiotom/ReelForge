using ReelForge.Shared.Data.Models;

namespace ReelForge.WorkflowEngine.Execution;

public interface IWorkflowEventPublisher
{
    Task PublishExecutionRunningAsync(WorkflowExecution execution, CancellationToken ct);

    Task PublishStepStartedAsync(
        WorkflowExecution execution,
        WorkflowStep step,
        WorkflowStepResult stepResult,
        string? inputPreview,
        CancellationToken ct);

    Task PublishStepCompletedAsync(
        WorkflowExecution execution,
        WorkflowStep step,
        WorkflowStepResult stepResult,
        StepExecutionResult stepExecutionResult,
        CancellationToken ct);

    Task PublishStepDiagnosticsAsync(
        WorkflowExecution execution,
        WorkflowStep step,
        WorkflowStepResult stepResult,
        StepExecutionResult stepExecutionResult,
        CancellationToken ct);

    Task PublishExecutionCompletedAsync(WorkflowExecution execution, CancellationToken ct);
    Task PublishExecutionFailedAsync(WorkflowExecution execution, CancellationToken ct);
}
