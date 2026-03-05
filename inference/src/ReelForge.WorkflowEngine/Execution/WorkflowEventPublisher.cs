using MassTransit;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.IntegrationEvents;

namespace ReelForge.WorkflowEngine.Execution;

public class WorkflowEventPublisher : IWorkflowEventPublisher
{
    private readonly IPublishEndpoint _publishEndpoint;

    public WorkflowEventPublisher(IPublishEndpoint publishEndpoint) => _publishEndpoint = publishEndpoint;

    public Task PublishStepCompletedAsync(
        WorkflowExecution execution,
        WorkflowStep step,
        WorkflowStepResult stepResult,
        CancellationToken ct) =>
        _publishEndpoint.Publish(new WorkflowStepCompleted
        {
            ExecutionId = execution.Id,
            StepId = step.Id,
            StepResultId = stepResult.Id,
            CorrelationId = execution.CorrelationId,
            StepStatus = stepResult.Status.ToString(),
            TokensUsed = stepResult.TokensUsed,
            DurationMs = stepResult.DurationMs,
            CompletedAt = stepResult.CompletedAt ?? DateTime.UtcNow
        }, ct);

    public Task PublishExecutionCompletedAsync(WorkflowExecution execution, CancellationToken ct) =>
        _publishEndpoint.Publish(new WorkflowExecutionCompleted
        {
            ExecutionId = execution.Id,
            ProjectId = execution.ProjectId,
            CorrelationId = execution.CorrelationId,
            FinalStatus = execution.Status.ToString(),
            ResultJson = execution.ResultJson,
            CompletedAt = execution.CompletedAt ?? DateTime.UtcNow
        }, ct);

    public Task PublishExecutionFailedAsync(WorkflowExecution execution, CancellationToken ct) =>
        _publishEndpoint.Publish(new WorkflowExecutionFailed
        {
            ExecutionId = execution.Id,
            CorrelationId = execution.CorrelationId,
            ErrorMessage = execution.ErrorMessage ?? "Unknown workflow execution failure.",
            FailedAt = execution.CompletedAt ?? DateTime.UtcNow
        }, ct);
}
