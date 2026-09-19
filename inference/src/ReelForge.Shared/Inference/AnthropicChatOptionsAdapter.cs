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
/// <b>Sampling parameters.</b> Anthropic marks <c>temperature</c>, <c>top_p</c> and <c>top_k</c>
/// deprecated, and the SDK's own <c>[Obsolete]</c> text is explicit that this is not merely
/// advisory: "Models released after Claude Opus 4.6 do not support setting temperature. A value of
/// 1.0 will be accepted for backwards compatibility, all other values will be rejected with a 400
/// error" (and likewise: any <c>top_k</c> is rejected, <c>top_p</c> is rejected below 0.99).
/// EVERY agent in this solution sets a <c>Temperature</c> (0.2–0.8) and some set <c>TopP</c>, so
/// forwarding them would make every agent run against a current Claude model fail with an HTTP 400.
/// </item>
/// <item>
/// <b>The raw representation.</b> <see cref="ChatOptions.RawRepresentationFactory"/> is how the
/// per-agent <c>ReasoningEffort</c> reaches the wire, and it produces an
/// <c>OpenAI.Chat.ChatCompletionOptions</c> — the wrong SDK's type. The Anthropic adapter does read
/// this property (its own docs offer it as the escape hatch for full control over thinking
/// configuration), and a foreign type there is at best silently ignored.
/// </item>
/// </list>
/// <para>
/// Doing this at the client boundary — the one place that knows the provider kind — keeps the Azure
/// and OpenAI-compatible paths byte-identical and needs no provider awareness in
/// <c>ReelForgeAgentBase</c>.
/// </para>
/// <para>
/// Consequences worth knowing. A per-agent temperature and reasoning effort are NOT applied on the
/// Anthropic path. For effort that is the safe default: ReelForge never sets
/// <see cref="ChatOptions.Reasoning"/>, so no <c>output_config.effort</c> is sent and the model
/// thinks at its own default effort under <c>thinking.type=adaptive</c> (the SDK's default mode).
/// Note that this means thinking is ON by default, and thinking tokens count against
/// <c>max_tokens</c> — see <c>ChatClientFactory.AnthropicDefaultMaxOutputTokens</c>. Forwarding
/// effort properly would mean mapping ReelForge's vLLM/Qwen-flavoured vocabulary
/// (<c>none/low/medium/xhigh</c>) onto <see cref="ReasoningOptions.Effort"/>, a deliberate
/// follow-up rather than part of this seam.
/// </para>
/// </remarks>
public sealed class AnthropicChatOptionsAdapter(IChatClient innerClient)
    : DelegatingChatClient(innerClient)
{
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
