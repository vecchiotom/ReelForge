using MassTransit;
using ReelForge.WorkflowEngine.Execution;
using ReelForge.Shared.IntegrationEvents;

namespace ReelForge.WorkflowEngine.Consumers;

/// <summary>
/// MassTransit consumer that listens for external requests to abort workflow
/// executions. The Go API publishes these events when a user presses the stop
/// button.
/// </summary>
public class WorkflowExecutionStopRequestedConsumer : IConsumer<WorkflowExecutionStopRequested>
{
    private readonly WorkflowExecutorService _executor;
    private readonly ILogger<WorkflowExecutionStopRequestedConsumer> _logger;

    public WorkflowExecutionStopRequestedConsumer(
        WorkflowExecutorService executor,
        ILogger<WorkflowExecutionStopRequestedConsumer> logger)
    {
        _executor = executor;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<WorkflowExecutionStopRequested> context)
    {
        var msg = context.Message;
        _logger.LogInformation("Received stop request for execution {ExecutionId} by user {UserId}",
            msg.ExecutionId, msg.RequestedByUserId);

        // fire and forget cancellation; result is logged/handled inside executor
        await _executor.CancelExecutionAsync(msg.ExecutionId, msg.RequestedByUserId);

        _logger.LogInformation("Stop request handled for execution {ExecutionId}", msg.ExecutionId);
    }
}