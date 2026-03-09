using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using ReelForge.Shared.IntegrationEvents;

namespace ReelForge.WorkflowEngine.Services.Messaging;

/// <summary>
/// Helper methods for interacting with RabbitMQ directly when MassTransit
/// doesn't expose the necessary control. Used primarily to remove a pending
/// workflow execution message from the queue when the corresponding execution
/// has been cancelled by the user.
/// </summary>
public class RabbitMqHelper
{
    private readonly IConfiguration _configuration;

    public RabbitMqHelper(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// Attempts to remove a message from the "workflow-execution" queue whose
    /// body contains the given execution id. This scans the queue using
    /// <c>BasicGet</c>, re-queuing messages that don't match so the original
    /// ordering is preserved. The scan stops when the queue is empty or the
    /// target message is found.
    /// </summary>
    /// <param name="executionId">ID of the execution to remove.</param>
    /// <returns>True if a matching message was found and removed, false
    /// otherwise.</returns>
    public virtual async Task<bool> RemoveExecutionMessageAsync(Guid executionId)
    {
        // RabbitMQ.Client is synchronous; wrap in Task.Run so callers can await
        return await Task.Run(async () =>
        {
            RabbitMQ.Client.ConnectionFactory factory = new RabbitMQ.Client.ConnectionFactory()
            {
                HostName = _configuration["RabbitMQ:Host"] ?? "localhost",
                UserName = _configuration["RabbitMQ:Username"] ?? "guest",
                Password = _configuration["RabbitMQ:Password"] ?? "guest",
            };

            // use the async variant to avoid blocking threadpool threads
            RabbitMQ.Client.IConnection connection = await factory.CreateConnectionAsync();
            // newer client exposes async channel creation; the returned IAsyncModel
            // still implements IModel so we can call BasicGet synchronously.
            using var channel = await connection.CreateChannelAsync();
            const string queueName = "workflow-execution";

            while (true)
            {
                // get a single message without acknowledging it
                var result = await channel.BasicGetAsync(queueName, autoAck: false);
                if (result == null)
                {
                    // queue empty
                    break;
                }

                try
                {
                    string json = Encoding.UTF8.GetString(result.Body.ToArray());
                    var msg = JsonSerializer.Deserialize<WorkflowExecutionRequested>(json);
                    if (msg != null && msg.ExecutionId == executionId)
                    {
                        // found the matching message; ack and return
                        await channel.BasicAckAsync(result.DeliveryTag, multiple: false);
                        return true;
                    }
                }
                catch
                {
                    // if we can't deserialize just requeue the message
                }

                // not the one we were looking for - requeue it at end of queue
                await channel.BasicNackAsync(result.DeliveryTag, multiple: false, requeue: true);
            }

            return false;
        });
    }
}