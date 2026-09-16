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

    /// <summary>
    /// Publishes an ephemeral <see cref="Shared.IntegrationEvents.WorkflowStepProgress"/> signal
    /// for a step that is still running. Best-effort only — never persisted, never awaited by the
    /// caller's own correctness (see the event's doc comment).
    /// </summary>
    Task PublishStepProgressAsync(
        WorkflowExecution execution,
        WorkflowStep step,
        WorkflowStepResult stepResult,
        string stage,
        int? percentComplete,
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
