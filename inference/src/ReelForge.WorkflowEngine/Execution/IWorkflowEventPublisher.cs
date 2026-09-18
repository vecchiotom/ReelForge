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

    /// <summary>
    /// Publishes an append-only <see cref="Shared.IntegrationEvents.WorkflowStepChatTurn"/> for one
    /// completed turn in a <c>StepType.EditRoom</c> group-chat run — see that event's doc comment
    /// for why this is a structural twin of <c>WorkflowStepReasoningCaptured</c>, not a reuse of
    /// the ephemeral/supersedable <see cref="PublishStepProgressAsync"/> event.
    /// </summary>
    Task PublishStepChatTurnAsync(
        WorkflowExecution execution,
        WorkflowStep step,
        WorkflowStepResult stepResult,
        int turnIndex,
        int? totalTurns,
        string speaker,
        string speakerRole,
        string text,
        IReadOnlyList<string> idsMentioned,
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
