using Microsoft.Extensions.AI;

namespace ReelForge.Inference.Api.Services.VectorSearch;

/// <summary>
/// Defers constructing the real embedding client (and validating its config, e.g. parsing
/// <c>AzureOpenAI:Endpoint</c> as a <see cref="Uri"/>) until an embedding is actually requested.
/// </summary>
/// <remarks>
/// Without this, registering the real client as a plain <c>AddSingleton</c> factory still throws
/// the instant anything resolves it — and <see cref="ReelForge.Inference.Api.Controllers.ProjectFilesController"/>
/// requires <c>VectorSearchQueryService</c>, which requires this generator, in its constructor.
/// ASP.NET Core builds a controller's full constructor dependency graph on every request, so a
/// missing/invalid Azure OpenAI config crashed every file-management endpoint (list, upload,
/// download, move, delete, reindex) — including ones that never touch vector search — rather than
/// degrading vector search alone (found by e2e QA).
/// </remarks>
public sealed class LazyEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly Lazy<IEmbeddingGenerator<string, Embedding<float>>> _inner;

    public LazyEmbeddingGenerator(Func<IEmbeddingGenerator<string, Embedding<float>>> factory)
    {
        _inner = new Lazy<IEmbeddingGenerator<string, Embedding<float>>>(() =>
        {
            try
            {
                return factory();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Vector search is unavailable: the embeddings backend (AzureOpenAI:Endpoint / " +
                    "AzureOpenAI:ApiKey / VectorSearch:EmbeddingDeployment) is not configured or is " +
                    "invalid. File upload/download/list/move/delete work normally without it; only " +
                    "semantic search and background vector indexing require it.", ex);
            }
        });
    }

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
        => _inner.Value.GenerateAsync(values, options, cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null)
        => _inner.Value.GetService(serviceType, serviceKey);

    public void Dispose()
    {
        if (_inner.IsValueCreated)
            _inner.Value.Dispose();
    }
}
