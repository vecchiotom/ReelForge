using Microsoft.Extensions.AI;

namespace ReelForge.Shared.Inference;

/// <summary>
/// Strips <see cref="ChatOptions.RawRepresentationFactory"/> before delegating to a non-OpenAI
/// <see cref="IChatClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// <c>ReelForgeAgentBase.BuildChatOptions</c> attaches an <c>OpenAI.Chat.ChatCompletionOptions</c>
/// through <see cref="ChatOptions.RawRepresentationFactory"/> for every agent that configures a
/// <c>ReasoningEffort</c> — that is the only publicly supported hook onto the wire-level
/// <c>reasoning_effort</c> field of a Chat Completions request. Those <see cref="ChatOptions"/> are
/// built once, in the agent's constructor, while the provider behind them is resolved per run, so
/// the agent cannot know whether the client it is about to talk to is OpenAI-shaped.
/// </para>
/// <para>
/// Handing an OpenAI-SDK-typed raw representation to a client that speaks a different wire protocol
/// is at best ignored and at worst a hard failure, and it would hit the majority of agents (most
/// pass a <c>ReasoningEffort</c> — <c>DirectorAgent</c> uses <c>"xhigh"</c>). Neutralising it here,
/// at the one boundary that actually knows the provider kind, keeps the OpenAI and Azure OpenAI
/// paths byte-identical while making every other kind safe by construction, without
/// <c>ReelForgeAgentBase</c> needing to grow provider awareness.
/// </para>
/// <para>
/// Consequence worth knowing: a per-agent <c>ReasoningEffort</c> is therefore NOT forwarded on a
/// stripped path. For <see cref="ReelForge.Shared.Data.Models.InferenceProviderKind.Anthropic"/>
/// that is the correct default anyway — <c>AnthropicThinkingMode.Adaptive</c> (the SDK default)
/// sends no thinking configuration at all, which is exactly how Anthropic documents current models
/// should be called, and it avoids the HTTP 400 that <c>thinking.type=disabled</c> returns on
/// models that always think. Forwarding effort properly would mean mapping ReelForge's
/// vLLM/Qwen-flavoured vocabulary (<c>none/low/medium/xhigh</c>) onto
/// <see cref="ReasoningOptions"/>, which is a deliberate follow-up rather than part of this seam.
/// </para>
/// </remarks>
public sealed class OpenAIRawOptionsStrippingChatClient(IChatClient innerClient)
    : DelegatingChatClient(innerClient)
{
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => base.GetResponseAsync(messages, Strip(options), cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => base.GetStreamingResponseAsync(messages, Strip(options), cancellationToken);

    /// <summary>
    /// Returns <paramref name="options"/> untouched when there is nothing to strip, so the common
    /// case allocates nothing. Otherwise clones — never mutates the caller's instance, which
    /// <c>ReelForgeAgentBase</c> holds for the lifetime of the agent and reuses on every run.
    /// </summary>
    private static ChatOptions? Strip(ChatOptions? options)
    {
        if (options?.RawRepresentationFactory is null)
            return options;

        ChatOptions stripped = options.Clone();
        stripped.RawRepresentationFactory = null;
        return stripped;
    }
}
