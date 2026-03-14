namespace ReelForge.Inference.Api.Services.VectorSearch;

public sealed record FileChunk(int ChunkIndex, int TotalChunks, string Language, string Content);

public sealed record VectorizedFileChunk(
    int ChunkIndex,
    int TotalChunks,
    string Language,
    string Content,
    ReadOnlyMemory<float> Vector);

public sealed record VectorSearchChunkResult(
    Guid FileId,
    string FilePath,
    string FileName,
    int ChunkIndex,
    int TotalChunks,
    string Language,
    string Content,
    float Score);
