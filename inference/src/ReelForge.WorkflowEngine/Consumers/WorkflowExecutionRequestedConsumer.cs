using MassTransit;
using ReelForge.Shared.IntegrationEvents;
using ReelForge.WorkflowEngine.Execution;

namespace ReelForge.WorkflowEngine.Consumers;

/// <summary>
/// MassTransit consumer that processes workflow execution requests from RabbitMQ.
/// </summary>
public class WorkflowExecutionRequestedConsumer : IConsumer<WorkflowExecutionRequested>
{
    private readonly WorkflowExecutorService _executor;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<WorkflowExecutionRequestedConsumer> _logger;

    public WorkflowExecutionRequestedConsumer(
        WorkflowExecutorService executor,
        IPublishEndpoint publishEndpoint,
        ILogger<WorkflowExecutionRequestedConsumer> logger)
    {
        _executor = executor;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<WorkflowExecutionRequested> context)
    {
        var message = context.Message;
        _logger.LogInformation(
            "Received workflow execution request: ExecutionId={ExecutionId}, CorrelationId={CorrelationId}",
            message.ExecutionId, message.CorrelationId);

        try
        {
            await _executor.ExecuteAsync(message.ExecutionId, message.CorrelationId, context.CancellationToken);

            await _publishEndpoint.Publish(new WorkflowExecutionCompleted
            {
                ExecutionId = message.ExecutionId,
                ProjectId = message.ProjectId,
                CorrelationId = message.CorrelationId,
                FinalStatus = "Passed",
                CompletedAt = DateTime.UtcNow
            }, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow execution {ExecutionId} failed", message.ExecutionId);

            await _publishEndpoint.Publish(new WorkflowExecutionFailed
            {
                ExecutionId = message.ExecutionId,
                CorrelationId = message.CorrelationId,
                ErrorMessage = ex.Message,
                FailedAt = DateTime.UtcNow
            }, context.CancellationToken);

            throw; // Let MassTransit handle retry/DLQ
        }
    }
}
