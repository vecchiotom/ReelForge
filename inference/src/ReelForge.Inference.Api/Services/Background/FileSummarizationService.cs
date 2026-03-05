using Microsoft.EntityFrameworkCore;
using ReelForge.Inference.Api.Agents;
using ReelForge.Inference.Api.Data;
using ReelForge.Inference.Api.Services.Storage;
using ReelForge.Shared.Data.Models;

namespace ReelForge.Inference.Api.Services.Background;

/// <summary>
/// Background service that processes file summarization tasks.
/// </summary>
public class FileSummarizationService : BackgroundService
{
    private readonly IBackgroundTaskQueue<FileSummarizationTask> _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FileSummarizationService> _logger;

    public FileSummarizationService(
        IBackgroundTaskQueue<FileSummarizationTask> queue,
        IServiceScopeFactory scopeFactory,
        ILogger<FileSummarizationService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("File summarization service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            FileSummarizationTask task = await _queue.DequeueAsync(stoppingToken);

            try
            {
                await ProcessAsync(task, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing file summarization for file {FileId}", task.FileId);
            }
        }
    }

    private async Task ProcessAsync(FileSummarizationTask task, CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        InferenceApiDbContext db = scope.ServiceProvider.GetRequiredService<InferenceApiDbContext>();
        IAgentRegistry agentRegistry = scope.ServiceProvider.GetRequiredService<IAgentRegistry>();
        IFileStorageService fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();

        ProjectFile? file = await db.ProjectFiles.FirstOrDefaultAsync(f => f.Id == task.FileId, ct);
        if (file == null)
        {
            _logger.LogWarning("File {FileId} not found for summarization", task.FileId);
            return;
        }

        file.SummaryStatus = SummaryStatus.Processing;
        await db.SaveChangesAsync(ct);

        try
        {
            using Stream stream = await fileStorage.DownloadAsync(file.StorageKey, ct);
            using StreamReader reader = new(stream);
            string content = await reader.ReadToEndAsync(ct);

            IReelForgeAgent? summarizer = agentRegistry.GetByType(AgentType.FileSummarizerAgent);
            if (summarizer == null)
            {
                _logger.LogWarning("FileSummarizerAgent not registered");
                file.SummaryStatus = SummaryStatus.Failed;
                await db.SaveChangesAsync(ct);
                return;
            }

            string summary = await summarizer.RunAsync(
                $"Summarize this file ({file.OriginalFileName}):\n\n{content}", ct);

            file.AgentSummary = summary;
            file.SummaryStatus = SummaryStatus.Done;
            await db.SaveChangesAsync(ct);

            _logger.LogInformation("Summarized file {FileId}: {FileName}", task.FileId, file.OriginalFileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to summarize file {FileId}", task.FileId);
            file.SummaryStatus = SummaryStatus.Failed;
            await db.SaveChangesAsync(ct);
        }
    }
}
