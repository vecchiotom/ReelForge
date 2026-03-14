using Microsoft.Extensions.Options;

namespace ReelForge.Inference.Api.Services.VectorSearch;

public sealed class SimpleTokenChunker : IFileChunker
{
    private readonly VectorSearchOptions _options;

    public SimpleTokenChunker(IOptions<VectorSearchOptions> options)
    {
        _options = options.Value;
    }

    public IReadOnlyList<FileChunk> Chunk(string content, string fileName)
    {
        if (string.IsNullOrWhiteSpace(content))
            return [];

        string language = InferLanguage(fileName);
        string[] tokens = content
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length == 0)
            return [];

        if (tokens.Length <= _options.SmallFileThresholdTokens)
        {
            return [new FileChunk(0, 1, language, content.Trim())];
        }

        int chunkSize = Math.Max(1, _options.ChunkSizeTokens);
        int overlap = Math.Clamp(_options.ChunkOverlapTokens, 0, chunkSize - 1);
        int step = Math.Max(1, chunkSize - overlap);

        List<string> chunkContents = [];
        for (int start = 0; start < tokens.Length; start += step)
        {
            int length = Math.Min(chunkSize, tokens.Length - start);
            if (length <= 0)
                break;

            string chunkText = string.Join(' ', tokens, start, length);
            chunkContents.Add(chunkText);

            if (start + length >= tokens.Length)
                break;
        }

        int totalChunks = chunkContents.Count;
        List<FileChunk> chunks = new(totalChunks);
        for (int index = 0; index < totalChunks; index++)
        {
            chunks.Add(new FileChunk(index, totalChunks, language, chunkContents[index]));
        }

        return chunks;
    }

    private static string InferLanguage(string fileName)
    {
        string extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".cs" => "csharp",
            ".go" => "go",
            ".ts" => "typescript",
            ".tsx" => "typescriptreact",
            ".js" => "javascript",
            ".jsx" => "javascriptreact",
            ".py" => "python",
            ".json" => "json",
            ".md" => "markdown",
            ".css" => "css",
            ".html" => "html",
            ".sql" => "sql",
            ".xml" => "xml",
            ".yml" or ".yaml" => "yaml",
            _ => "plaintext"
        };
    }
}
