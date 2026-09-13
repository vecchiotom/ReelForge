using System.ClientModel;
using System.Collections.Concurrent;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using OpenAI;
using ReelForge.Shared.Data.Models;

namespace ReelForge.Shared.Inference;

/// <summary>
/// Constructs <see cref="IChatClient"/> instances for either an Azure OpenAI or an
/// OpenAI-compatible (vLLM, LM Studio, llama.cpp server, LiteLLM, ...) backend, and caches
/// them by <see cref="ResolvedInferenceProvider.CacheKey"/> so repeated resolutions of the
/// same configuration reuse the same client/HttpClient.
/// </summary>
public sealed class ChatClientFactory : IChatClientFactory
{
    /// <summary>
    /// <see cref="ApiKeyCredential"/> throws on a null/empty string, but OpenAI-compatible
    /// self-hosted deployments (vLLM in particular) commonly run with no key at all.
    /// Substituting a placeholder here is mandatory, not cosmetic (Risk R5).
    /// </summary>
    private const string NoKeyPlaceholder = "not-required";

    private readonly ConcurrentDictionary<string, IChatClient> _clients = new();

    public IChatClient Get(ResolvedInferenceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return _clients.GetOrAdd(provider.CacheKey, _ => Build(provider));
    }

    private static IChatClient Build(ResolvedInferenceProvider provider) => provider.Kind switch
    {
        InferenceProviderKind.AzureOpenAI => BuildAzureOpenAI(provider),
        InferenceProviderKind.OpenAICompatible => BuildOpenAICompatible(provider),
        _ => throw new NotSupportedException($"Unsupported inference provider kind '{provider.Kind}'.")
    };

    private static IChatClient BuildAzureOpenAI(ResolvedInferenceProvider provider)
    {
        AzureOpenAIClientOptions options = new()
        {
            NetworkTimeout = TimeSpan.FromSeconds(provider.TimeoutSeconds)
        };

        AzureOpenAIClient client = new(
            new Uri(provider.Endpoint),
            new ApiKeyCredential(provider.ApiKey),
            options);

        return client.GetChatClient(provider.ModelName).AsIChatClient();
    }

    private static IChatClient BuildOpenAICompatible(ResolvedInferenceProvider provider)
    {
        string apiKey = string.IsNullOrWhiteSpace(provider.ApiKey) ? NoKeyPlaceholder : provider.ApiKey;

        OpenAIClientOptions options = new()
        {
            Endpoint = new Uri(provider.Endpoint),
            NetworkTimeout = TimeSpan.FromSeconds(provider.TimeoutSeconds)
        };

        OpenAIClient client = new(
            new ApiKeyCredential(apiKey),
            options);

        return client.GetChatClient(provider.ModelName).AsIChatClient();
    }
}
