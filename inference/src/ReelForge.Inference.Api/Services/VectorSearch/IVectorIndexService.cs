namespace ReelForge.Inference.Api.Services.VectorSearch;

public interface IVectorIndexService
{
    Task UpsertFileChunksAsync(
        Guid projectId,
        Guid fileId,
        string filePath,
        string fileName,
        IReadOnlyList<VectorizedFileChunk> chunks,
        CancellationToken ct);

    Task DeleteFileChunksAsync(Guid projectId, Guid fileId, CancellationToken ct);

    Task<IReadOnlyList<VectorSearchChunkResult>> SearchAsync(
        Guid projectId,
        ReadOnlyMemory<float> queryVector,
        int limit,
        CancellationToken ct);
}
