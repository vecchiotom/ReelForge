namespace ReelForge.Inference.Api.Services.VectorSearch;

public interface IFileChunker
{
    IReadOnlyList<FileChunk> Chunk(string content, string fileName);
}
