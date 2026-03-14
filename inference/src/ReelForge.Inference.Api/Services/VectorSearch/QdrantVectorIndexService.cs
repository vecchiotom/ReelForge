using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using static Qdrant.Client.Grpc.Conditions;

namespace ReelForge.Inference.Api.Services.VectorSearch;

public sealed class QdrantVectorIndexService : IVectorIndexService
{
    private readonly QdrantClient _client;
    private readonly ILogger<QdrantVectorIndexService> _logger;

    public QdrantVectorIndexService(IOptions<VectorSearchOptions> options, ILogger<QdrantVectorIndexService> logger)
    {
        VectorSearchOptions vectorSearchOptions = options.Value;
        Uri qdrantUri = new(vectorSearchOptions.QdrantUrl);
        _client = new QdrantClient(
            qdrantUri.Host,
            vectorSearchOptions.QdrantGrpcPort,
            qdrantUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase));
        _logger = logger;
    }

    public async Task UpsertFileChunksAsync(
        Guid projectId,
        Guid fileId,
        string filePath,
        string fileName,
        IReadOnlyList<VectorizedFileChunk> chunks,
        CancellationToken ct)
    {
        if (chunks.Count == 0)
            return;

        string collectionName = GetCollectionName(projectId);
        int vectorSize = chunks[0].Vector.Length;

        await EnsureCollectionForUpsertAsync(collectionName, vectorSize, ct);

        List<PointStruct> points = new(chunks.Count);
        foreach (VectorizedFileChunk chunk in chunks)
        {
            PointStruct point = new()
            {
                Id = Guid.NewGuid(),
                Vectors = chunk.Vector.ToArray()
            };

            point.Payload["projectId"] = projectId.ToString();
            point.Payload["fileId"] = fileId.ToString();
            point.Payload["filePath"] = filePath;
            point.Payload["fileName"] = fileName;
            point.Payload["chunkIndex"] = chunk.ChunkIndex;
            point.Payload["totalChunks"] = chunk.TotalChunks;
            point.Payload["language"] = chunk.Language;
            point.Payload["content"] = chunk.Content;

            points.Add(point);
        }

        try
        {
            await _client.UpsertAsync(collectionName, points, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            throw new IndexNotReadyException($"Vector index is not ready for project '{projectId}'.", ex);
        }
    }

    public async Task DeleteFileChunksAsync(Guid projectId, Guid fileId, CancellationToken ct)
    {
        string collectionName = GetCollectionName(projectId);
        await EnsureCollectionExistsAsync(collectionName, projectId, ct);

        try
        {
            Filter filter = MatchKeyword("fileId", fileId.ToString());
            await _client.DeleteAsync(collectionName, filter, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            throw new IndexNotReadyException($"Vector index is not ready for project '{projectId}'.", ex);
        }
    }

    public async Task<IReadOnlyList<VectorSearchChunkResult>> SearchAsync(
        Guid projectId,
        ReadOnlyMemory<float> queryVector,
        int limit,
        CancellationToken ct)
    {
        string collectionName = GetCollectionName(projectId);
        await EnsureCollectionExistsAsync(collectionName, projectId, ct);

        try
        {
            IReadOnlyList<ScoredPoint> results = await _client.SearchAsync(
                collectionName,
                queryVector,
                limit: (ulong)Math.Max(1, limit),
                cancellationToken: ct);

            return results.Select(MapResult).ToList();
        }
        catch (Exception ex)
        {
            throw new IndexNotReadyException($"Vector index is not ready for project '{projectId}'.", ex);
        }
    }

    private async Task EnsureCollectionForUpsertAsync(string collectionName, int vectorSize, CancellationToken ct)
    {
        try
        {
            bool exists = await _client.CollectionExistsAsync(collectionName, ct);
            if (exists)
                return;

            await _client.CreateCollectionAsync(collectionName, new VectorParams
            {
                Size = (ulong)vectorSize,
                Distance = Distance.Cosine
            }, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            throw new IndexNotReadyException($"Vector index is not ready for collection '{collectionName}'.", ex);
        }
    }

    private async Task EnsureCollectionExistsAsync(string collectionName, Guid projectId, CancellationToken ct)
    {
        try
        {
            bool exists = await _client.CollectionExistsAsync(collectionName, ct);
            if (!exists)
                throw new IndexNotReadyException($"Vector index is not ready for project '{projectId}'.");
        }
        catch (IndexNotReadyException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new IndexNotReadyException($"Vector index is not ready for project '{projectId}'.", ex);
        }
    }

    private static string GetCollectionName(Guid projectId)
        => projectId.ToString("N");

    private static VectorSearchChunkResult MapResult(ScoredPoint point)
    {
        Guid fileId = TryGetGuid(point.Payload, "fileId");
        string filePath = TryGetString(point.Payload, "filePath");
        string fileName = TryGetString(point.Payload, "fileName");
        int chunkIndex = TryGetInt(point.Payload, "chunkIndex");
        int totalChunks = TryGetInt(point.Payload, "totalChunks");
        string language = TryGetString(point.Payload, "language");
        string content = TryGetString(point.Payload, "content");

        return new VectorSearchChunkResult(
            fileId,
            filePath,
            fileName,
            chunkIndex,
            totalChunks,
            language,
            content,
            point.Score);
    }

    private static string TryGetString(IDictionary<string, Value> payload, string key)
    {
        if (!payload.TryGetValue(key, out Value? value) || value is null)
            return string.Empty;

        if (value.HasStringValue)
            return value.StringValue;

        if (value.HasIntegerValue)
            return value.IntegerValue.ToString();

        if (value.HasDoubleValue)
            return value.DoubleValue.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (value.HasBoolValue)
            return value.BoolValue.ToString();

        return string.Empty;
    }

    private static int TryGetInt(IDictionary<string, Value> payload, string key)
    {
        if (!payload.TryGetValue(key, out Value? value) || value is null)
            return 0;

        if (value.HasIntegerValue)
            return (int)value.IntegerValue;

        if (value.HasStringValue && int.TryParse(value.StringValue, out int parsed))
            return parsed;

        return 0;
    }

    private static Guid TryGetGuid(IDictionary<string, Value> payload, string key)
    {
        if (!payload.TryGetValue(key, out Value? value) || value is null)
            return Guid.Empty;

        if (!value.HasStringValue)
            return Guid.Empty;

        return Guid.TryParse(value.StringValue, out Guid parsed) ? parsed : Guid.Empty;
    }
}
