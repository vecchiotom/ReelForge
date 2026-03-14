using MassTransit;
using ReelForge.WorkflowEngine.Execution;
using ReelForge.Shared.IntegrationEvents;

namespace ReelForge.WorkflowEngine.Consumers;

/// <summary>
/// MassTransit consumer that processes workflow execution requests from RabbitMQ.
/// </summary>
public class WorkflowExecutionRequestedConsumer : IConsumer<WorkflowExecutionRequested>
{
    private readonly WorkflowExecutorService _executor;
    private readonly ILogger<WorkflowExecutionRequestedConsumer> _logger;

    public WorkflowExecutionRequestedConsumer(
        WorkflowExecutorService executor,
        ILogger<WorkflowExecutionRequestedConsumer> logger)
    {
        _executor = executor;
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
            _logger.LogInformation(
                "Workflow execution request processed: ExecutionId={ExecutionId}, CorrelationId={CorrelationId}",
                message.ExecutionId,
                message.CorrelationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow execution {ExecutionId} failed", message.ExecutionId);

            throw; // Let MassTransit handle retry/DLQ
        }
    }
}
