namespace ReelForge.Inference.Api.Services.VectorSearch;

public class VectorSearchOptions
{
    public const string SectionName = "VectorSearch";

    public string QdrantUrl { get; set; } = "http://localhost:6333";
    public int QdrantGrpcPort { get; set; } = 6334;
    public string EmbeddingDeployment { get; set; } = "text-embedding-3-large";
    public int ChunkSizeTokens { get; set; } = 512;
    public int ChunkOverlapTokens { get; set; } = 64;
    public int SmallFileThresholdTokens { get; set; } = 1500;
}
