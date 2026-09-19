using MassTransit;
using ReelForge.WorkflowEngine.Execution;
using ReelForge.Shared.IntegrationEvents;

namespace ReelForge.WorkflowEngine.Consumers;

/// <summary>
/// MassTransit consumer that processes workflow execution requests from RabbitMQ.
///
/// <para>
/// This consumer <b>dispatches</b> — it does not execute. It hands the request to
/// <see cref="WorkflowExecutionRunner"/> and returns, so the RabbitMQ delivery is acknowledged
/// within milliseconds instead of being held open for the execution's entire duration. See
/// <see cref="WorkflowExecutionRunner"/>'s summary for the failure this prevents: awaiting the
/// workflow here made RabbitMQ's <c>consumer_timeout</c> a hard cap on how long any workflow was
/// allowed to take, and three real executions were destroyed at exactly that cap — and then
/// blamed on the user — before it was tracked down.
/// </para>
/// </summary>
public class WorkflowExecutionRequestedConsumer : IConsumer<WorkflowExecutionRequested>
{
    private readonly WorkflowExecutionRunner _runner;
    private readonly ILogger<WorkflowExecutionRequestedConsumer> _logger;

    public WorkflowExecutionRequestedConsumer(
        WorkflowExecutionRunner runner,
        ILogger<WorkflowExecutionRequestedConsumer> logger)
    {
        _runner = runner;
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
            // context.CancellationToken is passed as the ADMISSION token only — it governs the wait
            // for a concurrency slot, never the execution itself. That distinction is the fix: a
            // transport-level cancellation while we are still queueing costs nothing (the execution
            // is untouched and still Queued, so redelivery restarts it cleanly), whereas the same
            // token reaching the running execution is what used to shred hours of work.
            await _runner.DispatchAsync(message.ExecutionId, message.CorrelationId, context.CancellationToken);

            _logger.LogInformation(
                "Workflow execution dispatched to the background runner: ExecutionId={ExecutionId}, CorrelationId={CorrelationId}",
                message.ExecutionId,
                message.CorrelationId);
        }
        catch (OperationCanceledException)
        {
            // Cancelled while waiting for a slot. Nothing has started, so letting the broker
            // redeliver is exactly right.
            _logger.LogInformation(
                "Workflow execution {ExecutionId} was not admitted before its delivery was cancelled; it will be redelivered",
                message.ExecutionId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispatch workflow execution {ExecutionId}", message.ExecutionId);

            throw; // Let MassTransit handle retry/DLQ
        }
    }
}
