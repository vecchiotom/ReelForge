using Microsoft.Extensions.AI;

namespace ReelForge.Inference.Api.Services.VectorSearch;

public sealed class VectorSearchQueryService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly IVectorIndexService _vectorIndexService;

    public VectorSearchQueryService(
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IVectorIndexService vectorIndexService)
    {
        _embeddingGenerator = embeddingGenerator;
        _vectorIndexService = vectorIndexService;
    }

    public async Task<IReadOnlyList<VectorSearchChunkResult>> SearchProjectFilesAsync(
        Guid projectId,
        string query,
        int limit,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        Embedding<float> embedding = await _embeddingGenerator.GenerateAsync(query, cancellationToken: ct);
        return await _vectorIndexService.SearchAsync(projectId, embedding.Vector, limit, ct);
    }
}
