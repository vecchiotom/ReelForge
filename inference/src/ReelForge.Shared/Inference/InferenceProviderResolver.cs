using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ReelForge.Shared.Data.Models;

namespace ReelForge.Shared.Inference;

/// <summary>
/// TTL-cached resolver over the two small provider tables. Registered as a singleton but the
/// underlying <see cref="IInferenceProviderStore"/> is scoped (it depends on a per-request
/// DbContext), so a <see cref="IServiceScopeFactory"/> is used to create a short-lived scope on
/// every cache refresh — the same pattern <c>WorkflowExecutorService</c> already uses for
/// singleton-adjacent DbContext access.
/// </summary>
public sealed class InferenceProviderResolver : IInferenceProviderResolver
{
    private const string DefaultFallbackModelName = "gpt-4o-mini";
    private const string FallbackProviderName = "AzureOpenAI (config fallback)";
    private const int DefaultTimeoutSeconds = 300;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ISecretProtector _secretProtector;
    private readonly ILogger<InferenceProviderResolver> _logger;
    private readonly TimeSpan _cacheTtl;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private Snapshot? _snapshot;

    public InferenceProviderResolver(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ISecretProtector secretProtector,
        ILogger<InferenceProviderResolver> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _secretProtector = secretProtector;
        _logger = logger;

        int cacheSeconds = configuration.GetValue("Inference:ProviderCacheSeconds", 60);
        _cacheTtl = TimeSpan.FromSeconds(Math.Max(1, cacheSeconds));
    }

    public async Task<ResolvedInferenceProvider> ResolveAsync(AgentType agentType, Guid? agentDefinitionId, CancellationToken ct)
    {
        Snapshot snapshot = await GetSnapshotAsync(ct);

        InferenceProvider? provider = null;

        if (agentDefinitionId.HasValue &&
            snapshot.AgentOverrides.TryGetValue(agentDefinitionId.Value, out Guid? overrideProviderId) &&
            overrideProviderId.HasValue &&
            snapshot.ProvidersById.TryGetValue(overrideProviderId.Value, out InferenceProvider? overrideProvider))
        {
            // The store's LoadEnabledAsync only returns enabled providers, so a reference to a
            // disabled (or deleted) provider simply misses here and falls through below.
            provider = overrideProvider;
        }

        provider ??= snapshot.DefaultProvider;

        return provider != null ? ResolveFromProvider(provider) : ResolveConfigFallback();
    }

    private ResolvedInferenceProvider ResolveFromProvider(InferenceProvider provider)
    {
        string apiKey = string.Empty;

        if (!string.IsNullOrEmpty(provider.ApiKeyEncrypted))
        {
            if (_secretProtector.TryUnprotect(provider.ApiKeyEncrypted, out string plaintext))
            {
                apiKey = plaintext;
            }
            else
            {
                _logger.LogWarning(
                    "Could not decrypt the API key for inference provider '{ProviderName}' ({ProviderId}); proceeding without a key.",
                    provider.Name,
                    provider.Id);
            }
        }

        return new ResolvedInferenceProvider(
            provider.Id,
            provider.Name,
            provider.Kind,
            provider.Endpoint,
            provider.ModelName,
            apiKey,
            provider.TimeoutSeconds ?? DefaultTimeoutSeconds);
    }

    private ResolvedInferenceProvider ResolveConfigFallback()
    {
        string endpoint = _configuration["AzureOpenAI:Endpoint"] ?? string.Empty;
        string apiKey = _configuration["AzureOpenAI:ApiKey"] ?? string.Empty;
        string deploymentName = _configuration["AzureOpenAI:DeploymentName"] ?? DefaultFallbackModelName;

        return new ResolvedInferenceProvider(
            ProviderId: null,
            Name: FallbackProviderName,
            Kind: InferenceProviderKind.AzureOpenAI,
            Endpoint: endpoint,
            ModelName: deploymentName,
            ApiKey: apiKey,
            TimeoutSeconds: DefaultTimeoutSeconds);
    }

    private async Task<Snapshot> GetSnapshotAsync(CancellationToken ct)
    {
        Snapshot? current = _snapshot;
        if (current != null && DateTime.UtcNow - current.LoadedAt < _cacheTtl)
        {
            return current;
        }

        await _refreshLock.WaitAsync(ct);
        try
        {
            current = _snapshot;
            if (current != null && DateTime.UtcNow - current.LoadedAt < _cacheTtl)
            {
                return current;
            }

            using IServiceScope scope = _scopeFactory.CreateScope();
            IInferenceProviderStore store = scope.ServiceProvider.GetRequiredService<IInferenceProviderStore>();

            IReadOnlyList<InferenceProvider> enabled = await store.LoadEnabledAsync(ct);
            IReadOnlyDictionary<Guid, Guid?> overrides = await store.LoadAgentOverridesAsync(ct);

            Dictionary<Guid, InferenceProvider> providersById = enabled.ToDictionary(p => p.Id);
            InferenceProvider? defaultProvider = enabled.FirstOrDefault(p => p.IsDefault);

            Snapshot snapshot = new(providersById, overrides, defaultProvider, DateTime.UtcNow);
            _snapshot = snapshot;
            return snapshot;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private sealed record Snapshot(
        IReadOnlyDictionary<Guid, InferenceProvider> ProvidersById,
        IReadOnlyDictionary<Guid, Guid?> AgentOverrides,
        InferenceProvider? DefaultProvider,
        DateTime LoadedAt);
}
