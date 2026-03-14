using MassTransit;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.IntegrationEvents;

namespace ReelForge.WorkflowEngine.Execution;

public class WorkflowEventPublisher : IWorkflowEventPublisher
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<WorkflowEventPublisher> _logger;

    public WorkflowEventPublisher(IPublishEndpoint publishEndpoint, ILogger<WorkflowEventPublisher> logger)
    {
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public Task PublishStepCompletedAsync(
        WorkflowExecution execution,
        WorkflowStep step,
        WorkflowStepResult stepResult,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Publishing step completed event: ExecutionId={ExecutionId}, StepId={StepId}, Status={Status}",
            execution.Id,
            step.Id,
            stepResult.Status);

        return
        _publishEndpoint.Publish(new WorkflowStepCompleted
        {
            ExecutionId = execution.Id,
            StepId = step.Id,
            StepResultId = stepResult.Id,
            ProjectId = execution.ProjectId,
            WorkflowDefinitionId = execution.WorkflowDefinitionId,
            StepOrder = step.StepOrder,
            StepLabel = step.Label,
            StepType = step.StepType.ToString(),
            IterationNumber = stepResult.IterationNumber,
            AgentType = step.AgentDefinition?.AgentType.ToString(),
            CorrelationId = execution.CorrelationId,
            StepStatus = stepResult.Status.ToString(),
            TokensUsed = stepResult.TokensUsed,
            DurationMs = stepResult.DurationMs,
            CompletedAt = stepResult.CompletedAt ?? DateTime.UtcNow
        }, ct);
    }

    public Task PublishExecutionCompletedAsync(WorkflowExecution execution, CancellationToken ct)
    {
        _logger.LogInformation(
            "Publishing execution completed event: ExecutionId={ExecutionId}, FinalStatus={FinalStatus}",
            execution.Id,
            execution.Status);

        return
        _publishEndpoint.Publish(new WorkflowExecutionCompleted
        {
            ExecutionId = execution.Id,
            ProjectId = execution.ProjectId,
            WorkflowDefinitionId = execution.WorkflowDefinitionId,
            InitiatedByUserId = execution.InitiatedByUserId,
            CorrelationId = execution.CorrelationId,
            FinalStatus = execution.Status.ToString(),
            ResultJson = execution.ResultJson,
            CompletedAt = execution.CompletedAt ?? DateTime.UtcNow
        }, ct);
    }

    public Task PublishExecutionFailedAsync(WorkflowExecution execution, CancellationToken ct)
    {
        _logger.LogInformation(
            "Publishing execution failed event: ExecutionId={ExecutionId}, ErrorMessage={ErrorMessage}",
            execution.Id,
            execution.ErrorMessage ?? "Unknown workflow execution failure.");

        return
        _publishEndpoint.Publish(new WorkflowExecutionFailed
        {
            ExecutionId = execution.Id,
            ProjectId = execution.ProjectId,
            WorkflowDefinitionId = execution.WorkflowDefinitionId,
            InitiatedByUserId = execution.InitiatedByUserId,
            CorrelationId = execution.CorrelationId,
            ErrorMessage = execution.ErrorMessage ?? "Unknown workflow execution failure.",
            FailedAt = execution.CompletedAt ?? DateTime.UtcNow
        }, ct);
    }
}
