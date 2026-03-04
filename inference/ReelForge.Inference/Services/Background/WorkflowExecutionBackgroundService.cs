using ReelForge.Inference.Workflows;

namespace ReelForge.Inference.Services.Background;

/// <summary>
/// Background service that processes queued workflow executions.
/// </summary>
public class WorkflowExecutionBackgroundService : BackgroundService
{
    private readonly IBackgroundTaskQueue<WorkflowExecutionTask> _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WorkflowExecutionBackgroundService> _logger;

    public WorkflowExecutionBackgroundService(
        IBackgroundTaskQueue<WorkflowExecutionTask> queue,
        IServiceScopeFactory scopeFactory,
        ILogger<WorkflowExecutionBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Workflow execution service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            WorkflowExecutionTask task = await _queue.DequeueAsync(stoppingToken);

            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                WorkflowExecutorService executor = scope.ServiceProvider.GetRequiredService<WorkflowExecutorService>();
                await executor.ExecuteAsync(task.ExecutionId, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing workflow execution {ExecutionId}", task.ExecutionId);
            }
        }
    }
}
