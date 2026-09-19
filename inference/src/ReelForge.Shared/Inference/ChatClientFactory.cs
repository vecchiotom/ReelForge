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
    /// The default ceiling on generated tokens for an Anthropic request. No AGENT run sets
    /// <see cref="Microsoft.Extensions.AI.ChatOptions.MaxOutputTokens"/> (<c>BuildChatOptions</c>
    /// sets only Temperature/TopP/TopK), so this is what every agent run actually gets; the
    /// provider-test endpoints do set it per request, and that still overrides this.
    /// <para>
    /// Sized for the largest structured outputs the workflow engine produces (video edit decisions,
    /// motion-graphics plans) while staying well under the point where a NON-streaming request
    /// risks an HTTP timeout — agents run through <c>AIAgent.RunAsync</c>, which does not stream.
    /// It also has to leave room for thinking: the SDK's default <c>AnthropicThinkingMode.Adaptive</c>
    /// means the model thinks at its own default effort, and thinking tokens count against this
    /// ceiling.
    /// </para>
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

    /// <summary>
    /// Explicit opt-in to the Anthropic SDK's own credential resolution (<c>ANTHROPIC_API_KEY</c> /
    /// <c>ANTHROPIC_AUTH_TOKEN</c> / an <c>ant auth login</c> profile) instead of a key stored on
    /// the provider row. Store this literal as the row's API key to use it.
    /// </summary>
    /// <remarks>
    /// A sentinel rather than "an empty key means ambient", because that inference fails open in
    /// three ways that all look identical from the admin UI: a key saved as whitespace, a Data
    /// Protection key-ring mismatch (<c>InferenceProviderResolver</c> deliberately degrades a
    /// failed decrypt to an empty string rather than throwing), and a genuinely blank field. Under
    /// the inference, each of those silently reroutes billing to whatever credential happens to be
    /// in the container's environment — and since executing a workflow needs no admin rights, any
    /// authenticated user could then spend it. <c>AnthropicClient.ShouldAutoResolveCredentials</c>
    /// is get-only, so the SDK's fallback cannot simply be turned off; making the intent explicit
    /// here is what separates "the operator asked for ambient credentials" from "something went
    /// wrong with the stored key".
    /// </remarks>
    public const string AnthropicAmbientCredentialSentinel = "env:";

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
    /// so the result is wrapped in <see cref="AnthropicChatOptionsAdapter"/> — see that type for
    /// why an agent's sampling parameters and OpenAI-typed raw options must not reach it.
    /// </summary>
    private static IChatClient BuildAnthropic(ResolvedInferenceProvider provider)
    {
        string key = provider.ApiKey?.Trim() ?? string.Empty;
        bool useAmbient = string.Equals(key, AnthropicAmbientCredentialSentinel, StringComparison.OrdinalIgnoreCase);

        // Fail loudly rather than falling back to whatever credential happens to sit in the
        // container's environment. An empty key here is not "no credential needed" — unlike the
        // OpenAI-compatible arm, where a keyless self-hosted vLLM is a normal deployment — it means
        // the row is misconfigured, or its stored key could not be decrypted (see
        // AnthropicAmbientCredentialSentinel). Silently succeeding on someone else's account is a
        // far worse outcome than an error naming the provider.
        if (!useAmbient && key.Length == 0)
        {
            throw new InvalidOperationException(
                $"Inference provider '{provider.Name}' (kind Anthropic) has no usable API key. Set " +
                $"one on the provider, or store the literal '{AnthropicAmbientCredentialSentinel}' " +
                "as its key to deliberately use the ANTHROPIC_API_KEY / ANTHROPIC_AUTH_TOKEN " +
                "environment variables instead. If a key IS configured, it failed to decrypt — " +
                "check that both services share the same DataProtection:KeysPath (the `dpkeys` " +
                "volume).");
        }

        bool isOAuthToken = key.StartsWith(AnthropicOAuthTokenPrefix, StringComparison.OrdinalIgnoreCase);

        AnthropicClient client = new()
        {
            // At most one of these is set. Both are null only under the explicit ambient sentinel,
            // where the SDK resolves credentials itself. See docs/anthropic-provider.md.
            ApiKey = !useAmbient && !isOAuthToken ? key : null,
            AuthToken = !useAmbient && isOAuthToken ? key : null,

            // Null means the SDK's production default (https://api.anthropic.com). The API requires
            // a non-empty endpoint on create, so in practice this is always set; the null branch
            // covers a row written directly to the database or the legacy config fallback.
            BaseUrl = string.IsNullOrWhiteSpace(provider.Endpoint) ? null : provider.Endpoint.Trim(),

            Timeout = TimeSpan.FromSeconds(provider.TimeoutSeconds)
        };

        return new AnthropicChatOptionsAdapter(
            client.AsIChatClient(provider.ModelName, AnthropicDefaultMaxOutputTokens));
    }
}
