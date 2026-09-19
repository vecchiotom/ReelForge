using Microsoft.Extensions.AI;

namespace ReelForge.Shared.Inference;

/// <summary>
/// Removes the <see cref="ChatOptions"/> settings that Claude cannot accept, before delegating to
/// an Anthropic <see cref="IChatClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// <c>ReelForgeAgentBase.BuildChatOptions</c> builds ONE <see cref="ChatOptions"/> instance per
/// agent, in the agent's constructor, and every agent is registered as a singleton — while the
/// provider behind it is resolved per run. So the agent cannot know whether it is about to talk to
/// an OpenAI-shaped backend or to Claude, and the options it produces are shaped for the former.
/// Two distinct things have to be dropped here:
/// </para>
/// <list type="number">
/// <item>
/// <b>Sampling parameters.</b> Anthropic deprecated <c>temperature</c>, <c>top_p</c> and
/// <c>top_k</c> server-side, and the SDK's own <c>[Obsolete]</c> text is explicit that this is not
/// merely advisory: "Models released after Claude Opus 4.6 do not support setting temperature. A
/// value of 1.0 will be accepted for backwards compatibility, all other values will be rejected
/// with a 400 error" (and likewise: any <c>top_k</c> is rejected, <c>top_p</c> is rejected below
/// 0.99). EVERY agent in this solution sets a <c>Temperature</c> (0.2–0.8) and some set
/// <c>TopP</c>, so forwarding them would make every agent run against a current Claude model fail
/// with an HTTP 400.
/// </item>
/// <item>
/// <b>The raw representation.</b> <see cref="ChatOptions.RawRepresentationFactory"/> is how the
/// per-agent <c>ReasoningEffort</c> reaches the wire, and it produces an
/// <c>OpenAI.Chat.ChatCompletionOptions</c> — the wrong SDK's type. Anthropic's adapter documents
/// this property as its own escape hatch for provider-native options, so handing it a foreign SDK's
/// type is at best silently ignored. Nothing is lost by dropping it: it carries only the
/// <c>ReasoningEffort</c>, which the Anthropic path does not forward anyway.
/// </item>
/// </list>
/// <para>
/// Doing this at the client boundary — the one place that knows the provider kind — keeps the Azure
/// and OpenAI-compatible paths byte-identical and needs no provider awareness in
/// <c>ReelForgeAgentBase</c>.
/// </para>
/// <para>
/// Consequences worth knowing. A per-agent temperature and reasoning effort are NOT applied on the
/// Anthropic path: ReelForge never sets <see cref="ChatOptions.Reasoning"/>, so no
/// <c>output_config.effort</c> is sent. Under the SDK's default thinking mode the model still
/// thinks, at its own default effort, and those tokens count against <c>max_tokens</c> — see
/// <c>ChatClientFactory.AnthropicDefaultMaxOutputTokens</c>. Nothing here ever sets
/// <see cref="ReasoningEffort.None"/> either: that maps to <c>thinking.type=disabled</c>, which
/// models that always think reject with a 400. Forwarding effort properly would mean mapping
/// ReelForge's vLLM/Qwen-flavoured vocabulary (<c>none/low/medium/xhigh</c>) onto
/// <see cref="ReasoningOptions.Effort"/>, a deliberate follow-up rather than part of this seam.
/// </para>
/// </remarks>
public sealed class AnthropicChatOptionsAdapter : DelegatingChatClient
{
    private readonly IDisposable? _ownedClient;

    /// <param name="innerClient">The SDK's <see cref="IChatClient"/> adapter to delegate to.</param>
    /// <param name="ownedClient">
    /// The underlying <c>AnthropicClient</c>, whose lifetime this wrapper takes over.
    /// <para>
    /// It has to be passed explicitly because nothing else disposes it: the SDK's own
    /// <c>AnthropicChatClient.Dispose</c> is an empty method (verified in the assembly — its IL
    /// body is a bare <c>ret</c>), so the adapter returned by <c>AsIChatClient</c> does not own the
    /// client it wraps. Without this the <c>AnthropicClient</c> — which is
    /// <see cref="IDisposable"/>, and holds an <c>HttpClient</c> — would be created and never
    /// released, which is what CodeQL's "Missing Dispose call on local IDisposable" flags.
    /// </para>
    /// <para>
    /// Note this only makes ownership explicit and correct; it does not change when disposal
    /// happens today. <c>ChatClientFactory</c> caches every client it builds for the lifetime of
    /// the process and is not itself disposable, which is deliberate — these are connection-pooled
    /// clients shared across executions, exactly like the two OpenAI arms.
    /// </para>
    /// </param>
    public AnthropicChatOptionsAdapter(IChatClient innerClient, IDisposable? ownedClient = null)
        : base(innerClient)
        => _ownedClient = ownedClient;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _ownedClient?.Dispose();
        }

        base.Dispose(disposing);
    }

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => base.GetResponseAsync(messages, Normalize(options), cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => base.GetStreamingResponseAsync(messages, Normalize(options), cancellationToken);

    /// <summary>
    /// Returns <paramref name="options"/> untouched when there is nothing to remove, so the case
    /// with nothing to do allocates nothing. Otherwise clones — never mutates the caller's
    /// instance, which is a singleton agent's field shared across concurrent executions and across
    /// providers. Mutating it would silently strip sampling settings from that agent's later runs
    /// against OpenAI too.
    /// </summary>
    private static ChatOptions? Normalize(ChatOptions? options)
    {
        if (options is null)
            return null;

        bool needsChange = options.RawRepresentationFactory is not null
            || options.Temperature is not null
            || options.TopP is not null
            || options.TopK is not null;

        if (!needsChange)
            return options;

        ChatOptions normalized = options.Clone();
        normalized.RawRepresentationFactory = null;
        normalized.Temperature = null;
        normalized.TopP = null;
        normalized.TopK = null;
        return normalized;
    }
}
