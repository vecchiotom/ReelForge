using System.ClientModel;
using System.Collections.Concurrent;
using Anthropic;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using OpenAI;
using ReelForge.Shared.Data.Models;

namespace ReelForge.Shared.Inference;

/// <summary>
/// Constructs <see cref="IChatClient"/> instances for an Azure OpenAI, an OpenAI-compatible
/// (vLLM, LM Studio, llama.cpp server, LiteLLM, ...), or an Anthropic backend, and caches
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

    /// <summary>
    /// Anthropic's Messages API requires <c>max_tokens</c> on every request, unlike Chat
    /// Completions where it is optional. Nothing in this codebase sets
    /// <see cref="Microsoft.Extensions.AI.ChatOptions.MaxOutputTokens"/> (<c>BuildChatOptions</c>
    /// sets only Temperature/TopP/TopK), so the value supplied here is what every agent run
    /// actually gets. It is sized for the largest structured outputs the workflow engine produces
    /// (video edit decisions, motion-graphics plans) while staying well under the point where a
    /// non-streaming request risks an HTTP timeout — agents are run through
    /// <c>AIAgent.RunAsync</c>, which does not stream. A per-request
    /// <see cref="Microsoft.Extensions.AI.ChatOptions.MaxOutputTokens"/> still overrides it.
    /// </summary>
    private const int AnthropicDefaultMaxOutputTokens = 16_384;

    /// <summary>
    /// Anthropic OAuth tokens (as minted by <c>claude setup-token</c>) carry an <c>oat</c> —
    /// "OAuth token" — marker in their prefix and must travel as an <c>Authorization: Bearer</c>
    /// header, whereas a normal console API key travels as <c>x-api-key</c>. Sending either down
    /// the other path fails authentication, and the stored column cannot tell them apart on its
    /// own, so the prefix is the discriminator. See docs/anthropic-provider.md.
    /// </summary>
    private const string AnthropicOAuthTokenPrefix = "sk-ant-oat";

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
        InferenceProviderKind.Anthropic => BuildAnthropic(provider),
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

    /// <summary>
    /// Builds a client against Anthropic's first-party Messages API through the official
    /// <c>Anthropic</c> SDK. Unlike the two OpenAI arms this is not a Chat Completions endpoint,
    /// so the result is wrapped in <see cref="OpenAIRawOptionsStrippingChatClient"/> — see that
    /// type for why an agent's OpenAI-typed raw options must not reach it.
    /// </summary>
    private static IChatClient BuildAnthropic(ResolvedInferenceProvider provider)
    {
        string key = provider.ApiKey?.Trim() ?? string.Empty;
        bool isOAuthToken = key.StartsWith(AnthropicOAuthTokenPrefix, StringComparison.OrdinalIgnoreCase);

        AnthropicClient client = new()
        {
            // Exactly one of these is ever set. Leaving BOTH null is meaningful rather than
            // broken: the SDK then falls back to its own credential resolution
            // (ANTHROPIC_API_KEY / ANTHROPIC_AUTH_TOKEN / an `ant auth login` profile), which is
            // how a developer runs against their own credentials without persisting a secret in
            // the database at all. See docs/anthropic-provider.md.
            ApiKey = key.Length > 0 && !isOAuthToken ? key : null,
            AuthToken = key.Length > 0 && isOAuthToken ? key : null,

            // Null means the SDK's production default (https://api.anthropic.com). Endpoint is
            // optional for this kind precisely so the common case needs no value; a non-empty one
            // points at a gateway.
            BaseUrl = string.IsNullOrWhiteSpace(provider.Endpoint) ? null : provider.Endpoint.Trim(),

            Timeout = TimeSpan.FromSeconds(provider.TimeoutSeconds)
        };

        return new OpenAIRawOptionsStrippingChatClient(
            client.AsIChatClient(provider.ModelName, AnthropicDefaultMaxOutputTokens));
    }
}
