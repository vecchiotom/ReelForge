using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using ReelForge.Inference.Api.Data;
using ReelForge.Inference.Api.Services.Storage;
using ReelForge.Inference.Api.Services.VectorSearch;
using ReelForge.Shared.IntegrationEvents;

namespace ReelForge.Inference.Api.Consumers;

public sealed class ProjectFileIndexingConsumer : IConsumer<ProjectFileIndexingRequested>
{
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

        Shared.Data.Models.ProjectFile? file = await _db.ProjectFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == message.FileId && f.ProjectId == message.ProjectId, context.CancellationToken);

        if (file is null)
        {
            _logger.LogInformation("Project file {FileId} no longer exists; skipping vector upsert", message.FileId);
            return;
        }

        await using Stream stream = await _fileStorage.DownloadAsync(message.ProjectId, file.StorageKey, context.CancellationToken);
        using StreamReader reader = new(stream);
        string content = await reader.ReadToEndAsync(context.CancellationToken);

        IReadOnlyList<FileChunk> chunks = _fileChunker.Chunk(content, file.OriginalFileName);
        if (chunks.Count == 0)
            return;

        List<VectorizedFileChunk> vectorizedChunks = new(chunks.Count);
        foreach (FileChunk chunk in chunks)
        {
            Embedding<float> embedding = await _embeddingGenerator.GenerateAsync(chunk.Content, cancellationToken: context.CancellationToken);
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
    }
}
