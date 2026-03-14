using Microsoft.Extensions.AI;
using System.Linq;

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

        int normalizedLimit = Math.Clamp(limit <= 0 ? 5 : limit, 1, 20);
        List<string> queryVariants = BuildQueryVariants(query);
        int perVariantLimit = Math.Clamp(normalizedLimit * 2, normalizedLimit, 40);

        Dictionary<(Guid fileId, int chunkIndex), VectorSearchChunkResult> mergedResults = [];

        foreach (string variant in queryVariants)
        {
            Embedding<float> embedding = await _embeddingGenerator.GenerateAsync(variant, cancellationToken: ct);
            IReadOnlyList<VectorSearchChunkResult> variantResults = await _vectorIndexService.SearchAsync(projectId, embedding.Vector, perVariantLimit, ct);

            foreach (VectorSearchChunkResult result in variantResults)
            {
                var key = (result.FileId, result.ChunkIndex);
                if (!mergedResults.TryGetValue(key, out VectorSearchChunkResult? current) || result.Score > current.Score)
                {
                    mergedResults[key] = result;
                }
            }
        }

        string[] queryTokens = TokenizeForRanking(query);

        return mergedResults.Values
            .Select(result => new
            {
                Result = result,
                RankScore = ComputeRankScore(result, queryTokens)
            })
            .OrderByDescending(item => item.RankScore)
            .ThenByDescending(item => item.Result.Score)
            .Take(normalizedLimit)
            .Select(item => item.Result)
            .ToList();
    }

    private static List<string> BuildQueryVariants(string query)
    {
        string normalized = query.Trim();
        List<string> variants = [normalized];

        string[] tokens = TokenizeForRanking(normalized);
        if (tokens.Length == 0)
            return variants;

        string keywordsOnly = string.Join(' ', tokens.Take(8));
        if (!string.Equals(keywordsOnly, normalized, StringComparison.OrdinalIgnoreCase))
            variants.Add(keywordsOnly);

        string[] codeFocusedTerms = tokens
            .Where(token => token is "component" or "components" or "composition" or "compositions" or "timeline" or "sequence" or "transition" or "style" or "theme" or "remotion")
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (codeFocusedTerms.Length > 0)
        {
            string focused = string.Join(' ', codeFocusedTerms);
            if (!variants.Any(existing => string.Equals(existing, focused, StringComparison.OrdinalIgnoreCase)))
                variants.Add(focused);
        }

        return variants.Take(3).ToList();
    }

    private static string[] TokenizeForRanking(string text)
    {
        char[] separators = [' ', '\t', '\r', '\n', ',', '.', ';', ':', '(', ')', '[', ']', '{', '}', '/', '\\', '-', '_', '"', '\''];
        return text
            .ToLowerInvariant()
            .Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length >= 3)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static float ComputeRankScore(VectorSearchChunkResult result, string[] queryTokens)
    {
        if (queryTokens.Length == 0)
            return result.Score;

        string haystack = string.Join(' ', result.FilePath, result.FileName, result.Language, result.Content).ToLowerInvariant();
        int lexicalHits = queryTokens.Count(token => haystack.Contains(token, StringComparison.Ordinal));
        float lexicalBoost = lexicalHits / (float)queryTokens.Length;

        return (result.Score * 0.8f) + (lexicalBoost * 0.2f);
    }
}
