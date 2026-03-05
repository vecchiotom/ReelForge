using ReelForge.WorkflowEngine.Observability;

namespace ReelForge.WorkflowEngine.Workers;

/// <summary>
/// Background service that periodically logs workflow metrics.
/// </summary>
public class WorkflowWorkerPool : BackgroundService
{
    private readonly ILogger<WorkflowWorkerPool> _logger;

    public WorkflowWorkerPool(ILogger<WorkflowWorkerPool> logger) => _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WorkflowWorkerPool started");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            _logger.LogDebug("WorkflowWorkerPool health check - service running");
        }
    }
}
