using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using System.ClientModel;
using ReelForge.Inference.Api.Data;
using ReelForge.Inference.Api.Services.Storage;
using ReelForge.Inference.Api.Services.VectorSearch;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.IntegrationEvents;

namespace ReelForge.Inference.Api.Consumers;

public sealed class ProjectFileIndexingConsumer : IConsumer<ProjectFileIndexingRequested>
{
    private const int EmbeddingMaxAttempts = 5;
    private const double EmbeddingRetryBaseDelaySeconds = 1;
    private readonly InferenceApiDbContext _db;
    private readonly IFileStorageService _fileStorage;
    private readonly IFileChunker _fileChunker;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly IVectorIndexService _vectorIndexService;
    private readonly ILogger<ProjectFileIndexingConsumer> _logger;

    public ProjectFileIndexingConsumer(
        InferenceApiDbContext db,
        IFileStorageService fileStorage,
        IFileChunker fileChunker,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IVectorIndexService vectorIndexService,
        ILogger<ProjectFileIndexingConsumer> logger)
    {
        _db = db;
        _fileStorage = fileStorage;
        _fileChunker = fileChunker;
        _embeddingGenerator = embeddingGenerator;
        _vectorIndexService = vectorIndexService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ProjectFileIndexingRequested> context)
    {
        ProjectFileIndexingRequested message = context.Message;

        try
        {
            await _vectorIndexService.DeleteFileChunksAsync(message.ProjectId, message.FileId, context.CancellationToken);
        }
        catch (IndexNotReadyException ex)
        {
            _logger.LogDebug(ex, "Vector index not ready while deleting old vectors for file {FileId}", message.FileId);
        }

        if (string.Equals(message.Operation, "Delete", StringComparison.OrdinalIgnoreCase))
            return;

        if (!string.Equals(message.Operation, "Upsert", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Unsupported indexing operation {Operation} for file {FileId}", message.Operation, message.FileId);
            return;
        }

        ProjectFile? file = await _db.ProjectFiles
            .FirstOrDefaultAsync(f => f.Id == message.FileId && f.ProjectId == message.ProjectId, context.CancellationToken);

        if (file is null)
        {
            _logger.LogInformation("Project file {FileId} no longer exists; skipping vector upsert", message.FileId);
            return;
        }

        file.IndexingStatus = FileIndexingStatus.Processing;
        file.IndexingError = null;
        await _db.SaveChangesAsync(context.CancellationToken);

        try
        {
            await using Stream stream = await _fileStorage.DownloadAsync(message.ProjectId, file.StorageKey, context.CancellationToken);
            using StreamReader reader = new(stream);
            string content = await reader.ReadToEndAsync(context.CancellationToken);

            IReadOnlyList<FileChunk> chunks = _fileChunker.Chunk(content, file.OriginalFileName);
            if (chunks.Count == 0)
            {
                file.IndexingStatus = FileIndexingStatus.Indexed;
                file.IndexingError = null;
                file.IndexedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(context.CancellationToken);
                return;
            }

            List<VectorizedFileChunk> vectorizedChunks = new(chunks.Count);
            foreach (FileChunk chunk in chunks)
            {
                Embedding<float> embedding = await GenerateEmbeddingWithRetryAsync(
                    chunk,
                    message.FileId,
                    context.CancellationToken);

                vectorizedChunks.Add(new VectorizedFileChunk(
                    chunk.ChunkIndex,
                    chunk.TotalChunks,
                    chunk.Language,
                    chunk.Content,
                    embedding.Vector));
            }

            string filePath = file.OriginalPath ?? file.OriginalFileName;
            await _vectorIndexService.UpsertFileChunksAsync(
                message.ProjectId,
                message.FileId,
                filePath,
                file.OriginalFileName,
                vectorizedChunks,
                context.CancellationToken);

            file.IndexingStatus = FileIndexingStatus.Indexed;
            file.IndexingError = null;
            file.IndexedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(context.CancellationToken);
        }
        catch (Exception ex)
        {
            file.IndexingStatus = FileIndexingStatus.Failed;
            file.IndexingError = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            await _db.SaveChangesAsync(context.CancellationToken);
            throw;
        }
    }

    private async Task<Embedding<float>> GenerateEmbeddingWithRetryAsync(
        FileChunk chunk,
        Guid fileId,
        CancellationToken ct)
    {
        Exception? lastException = null;

        for (int attempt = 1; attempt <= EmbeddingMaxAttempts; attempt++)
        {
            try
            {
                return await _embeddingGenerator.GenerateAsync(chunk.Content, cancellationToken: ct);
            }
            catch (Exception ex) when (attempt < EmbeddingMaxAttempts && IsTransientEmbeddingException(ex))
            {
                lastException = ex;

                double delaySeconds = Math.Pow(2, attempt - 1) * EmbeddingRetryBaseDelaySeconds;
                int jitterMs = Random.Shared.Next(0, 250);
                TimeSpan delay = TimeSpan.FromSeconds(delaySeconds) + TimeSpan.FromMilliseconds(jitterMs);

                _logger.LogWarning(
                    ex,
                    "Embedding generation failed for file {FileId}, chunk {ChunkIndex}/{TotalChunks} on attempt {Attempt}/{MaxAttempts}. Retrying in {DelayMs}ms",
                    fileId,
                    chunk.ChunkIndex + 1,
                    chunk.TotalChunks,
                    attempt,
                    EmbeddingMaxAttempts,
                    (int)delay.TotalMilliseconds);

                await Task.Delay(delay, ct);
            }
        }

        _logger.LogError(
            lastException,
            "Embedding generation failed for file {FileId}, chunk {ChunkIndex}/{TotalChunks} after {MaxAttempts} attempts",
            fileId,
            chunk.ChunkIndex + 1,
            chunk.TotalChunks,
            EmbeddingMaxAttempts);

        throw lastException ?? new InvalidOperationException("Embedding generation failed after retries.");
    }

    private static bool IsTransientEmbeddingException(Exception ex)
    {
        Exception baseException = ex.GetBaseException();

        if (baseException is OperationCanceledException)
            return false;

        if (baseException is ClientResultException clientResultException)
        {
            return clientResultException.Status is 408 or 409 or 425 or 429 or >= 500;
        }

        if (baseException is HttpRequestException)
            return true;

        string message = baseException.Message;
        return message.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
            || message.Contains("too many requests", StringComparison.OrdinalIgnoreCase)
            || message.Contains("throttl", StringComparison.OrdinalIgnoreCase)
            || message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || message.Contains("temporar", StringComparison.OrdinalIgnoreCase)
            || message.Contains("unavailable", StringComparison.OrdinalIgnoreCase);
    }
}
