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
            snapshot.ProvidersById.TryGetValue(overrideProviderId.Value, out InferenceProvider? overrideProvider) &&
            overrideProvider.Capability == InferenceProviderCapability.Chat)
        {
            // The store's LoadEnabledAsync only returns enabled providers, so a reference to a
            // disabled (or deleted) provider simply misses here and falls through below. The
            // Capability check is the resolver's own defense in depth: the API/UI should never
            // let a Transcription provider be assigned as a chat override, but if one somehow
            // is (found by Copilot review), it must not be handed to a chat call — fall through
            // to the Chat default instead of building an IChatClient against an ASR endpoint.
            provider = overrideProvider;
        }

        provider ??= snapshot.DefaultChatProvider;

        return provider != null ? ResolveFromProvider(provider) : ResolveConfigFallback();
    }

    public async Task<ResolvedTranscriptionProvider?> ResolveTranscriptionAsync(Guid? explicitProviderId, CancellationToken ct)
    {
        Snapshot snapshot = await GetSnapshotAsync(ct);

        InferenceProvider? provider = null;

        if (explicitProviderId.HasValue &&
            snapshot.ProvidersById.TryGetValue(explicitProviderId.Value, out InferenceProvider? explicitProvider) &&
            explicitProvider.Capability == InferenceProviderCapability.Transcription)
        {
            // The store's LoadEnabledAsync only returns enabled providers, so a reference to a
            // disabled (or deleted) provider simply misses here and falls through to the default.
            // The Capability check guards the inverse gap (found by Copilot review): a Chat
            // provider id authored directly into VideoAnalyzeStepConfig.TranscriptionProviderId
            // (bypassing the UI, which only offers Transcription rows) must not be sent through
            // the transcription client factory.
            provider = explicitProvider;
        }

        provider ??= snapshot.DefaultTranscriptionProvider;

        return provider != null ? ResolveTranscriptionFromProvider(provider) : null;
    }

    public async Task<ResolvedInferenceProvider?> ResolveVisionAsync(Guid? explicitProviderId, CancellationToken ct)
    {
        Snapshot snapshot = await GetSnapshotAsync(ct);

        InferenceProvider? provider = null;

        if (explicitProviderId.HasValue &&
            snapshot.ProvidersById.TryGetValue(explicitProviderId.Value, out InferenceProvider? explicitProvider) &&
            explicitProvider.Capability == InferenceProviderCapability.Vision)
        {
            // Same defense-in-depth shape as ResolveTranscriptionAsync's Capability guard: a Chat
            // (or Transcription) provider id authored directly into
            // VideoAnalyzeStepConfig.VisionProviderId must not be sent through the vision path.
            provider = explicitProvider;
        }

        provider ??= snapshot.DefaultVisionProvider;

        // Vision reuses the exact same chat-completions client construction as ResolveAsync —
        // ResolveFromProvider, not a dedicated "ResolveVisionFromProvider" — since a vision call
        // is just a chat call with an image content part (see IInferenceProviderResolver.ResolveVisionAsync).
        return provider != null ? ResolveFromProvider(provider) : null;
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

    private ResolvedTranscriptionProvider ResolveTranscriptionFromProvider(InferenceProvider provider)
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

        return new ResolvedTranscriptionProvider(
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

            // R4: the capability split means "the default row" is no longer a single concept.
            // A Transcription-capability default must never be handed to the chat path (and vice
            // versa), so both lookups filter on Capability explicitly rather than sharing one
            // "the IsDefault row" default.
            InferenceProvider? defaultChatProvider = enabled.FirstOrDefault(
                p => p.IsDefault && p.Capability == InferenceProviderCapability.Chat);
            InferenceProvider? defaultTranscriptionProvider = enabled.FirstOrDefault(
                p => p.IsDefault && p.Capability == InferenceProviderCapability.Transcription);
            InferenceProvider? defaultVisionProvider = enabled.FirstOrDefault(
                p => p.IsDefault && p.Capability == InferenceProviderCapability.Vision);

            Snapshot snapshot = new(
                providersById, overrides, defaultChatProvider, defaultTranscriptionProvider,
                defaultVisionProvider, DateTime.UtcNow);
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
        InferenceProvider? DefaultChatProvider,
        InferenceProvider? DefaultTranscriptionProvider,
        InferenceProvider? DefaultVisionProvider,
        DateTime LoadedAt);
}
