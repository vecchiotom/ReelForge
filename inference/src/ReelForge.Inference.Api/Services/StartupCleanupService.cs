using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using ReelForge.Inference.Api.Data;
using ReelForge.Shared.Data.Models;

namespace ReelForge.Inference.Api.Services.Background;

/// <summary>
/// Performs one-time cleanup tasks when the Inference API starts.
/// - purges RabbitMQ queues that are used for workflow executions so stale
///   messages do not linger when the engine restarts.
/// - marks any previously running workflows as cancelled in the database so
///   the UI doesn't indefinitely show "Running".
/// </summary>
public class StartupCleanupService : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly IConfiguration _configuration;

    public StartupCleanupService(IServiceProvider services, IConfiguration configuration)
    {
        _services = services;
        _configuration = configuration;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await CancelRunningExecutionsAsync(cancellationToken);
        await PurgeRabbitMqQueuesAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task CancelRunningExecutionsAsync(CancellationToken ct)
    {
        // workflow_executions is owned and migrated by the WorkflowEngine service, not this one
        // (ExcludeFromMigrations() here — see InferenceApiDbContext). On a brand-new database,
        // the WorkflowEngine's migrations may not have run yet by the time this service starts,
        // so the table can genuinely not exist. That must not be fatal: compose only starts the
        // WorkflowEngine once this API reports healthy, so an unhandled exception here would
        // permanently deadlock a fresh stack (this API can never become healthy, so the engine's
        // migrations that would create the table never get a chance to run). This cleanup is
        // best-effort, exactly like the RabbitMQ purge below — swallow and log, never block startup.
        try
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<InferenceApiDbContext>();

            var toCancel = await db.WorkflowExecutions
                .Where(e => e.Status == ExecutionStatus.Running || e.Status == ExecutionStatus.Queued)
                .ToListAsync(ct);

            if (toCancel.Count == 0)
                return;

            foreach (var exec in toCancel)
            {
                exec.Status = ExecutionStatus.Cancelled;
                exec.ErrorMessage = "Service restarted";
                exec.CompletedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // non-critical; log via Console since no logger is available here (mirrors the
            // RabbitMQ purge's own error handling below)
            Console.WriteLine($"[startup-cleanup] failed to cancel stale executions: {ex.Message}");
        }
    }

    private async Task PurgeRabbitMqQueuesAsync(CancellationToken ct)
    {
        try
        {
            var factory = new RabbitMQ.Client.ConnectionFactory
            {
                HostName = _configuration["RabbitMQ:Host"] ?? "localhost",
                UserName = _configuration["RabbitMQ:Username"] ?? "guest",
                Password = _configuration["RabbitMQ:Password"] ?? "guest",
            };

            using var conn = await factory.CreateConnectionAsync();
            using var channel = await conn.CreateChannelAsync();

            // known queues used by the WorkflowEngine
            foreach (var queue in new[] { "workflow-execution", "workflow-stop-requests" })
            {
                try
                {
                    await channel.QueuePurgeAsync(queue);
                }
                catch
                {
                    // ignore if the queue doesn't exist yet
                }
            }
        }
        catch (Exception ex)
        {
            // non-critical; log via Console since no logger is available here
            Console.WriteLine($"[startup-cleanup] failed to purge queues: {ex.Message}");
        }

        return;
    }
}
