using FluentAssertions;
using Microsoft.Extensions.AI;
using ReelForge.Shared.Inference;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// <c>ReelForgeAgentBase.BuildChatOptions</c> builds one <see cref="ChatOptions"/> per agent, in
/// the constructor of a singleton, and reuses it on every run — long before the per-run provider is
/// known. It sets a Temperature for every agent (and sometimes TopP/TopK), plus an
/// <c>OpenAI.Chat.ChatCompletionOptions</c> through
/// <see cref="ChatOptions.RawRepresentationFactory"/> for any agent with a <c>ReasoningEffort</c>.
/// Claude rejects all three sampling parameters with an HTTP 400 on models released after Opus 4.6,
/// so these tests pin what makes routing such an agent to Anthropic safe: the rejected settings
/// never reach the inner client, and the caller's shared instance is never mutated in the process.
/// </summary>
public class AnthropicChatOptionsAdapterTests
{
    [Fact]
    public async Task GetResponseAsync_strips_the_raw_representation_factory()
    {
        RecordingChatClient inner = new();
        AnthropicChatOptionsAdapter client = new(inner);
        ChatOptions options = new() { RawRepresentationFactory = _ => new object() };

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], options);

        inner.LastOptions.Should().NotBeNull();
        inner.LastOptions!.RawRepresentationFactory.Should().BeNull();
    }

    [Fact]
    public async Task GetResponseAsync_does_not_mutate_the_callers_options()
    {
        // The agent holds this instance for its lifetime; mutating it would silently disable
        // reasoning_effort for every later run of that agent, including runs that resolve back to
        // an OpenAI provider.
        RecordingChatClient inner = new();
        AnthropicChatOptionsAdapter client = new(inner);
        Func<IChatClient, object?> factory = _ => new object();
        ChatOptions options = new() { RawRepresentationFactory = factory };

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], options);

        options.RawRepresentationFactory.Should().BeSameAs(factory);
        inner.LastOptions.Should().NotBeSameAs(options);
    }

    [Fact]
    public async Task GetResponseAsync_strips_the_sampling_parameters_Claude_rejects()
    {
        // Anthropic rejects these server-side: "Models released after Claude Opus 4.6 do not
        // support setting temperature. A value of 1.0 will be accepted for backwards compatibility,
        // all other values will be rejected with a 400 error" (the wording newer Anthropic SDK
        // releases carry as [Obsolete] on these properties) — and likewise any top_k, and top_p
        // below 0.99. Every agent in this solution sets a Temperature, so forwarding these would
        // 400 every single agent run against a current Claude model.
        RecordingChatClient inner = new();
        AnthropicChatOptionsAdapter client = new(inner);
        ChatOptions options = new() { Temperature = 0.7f, TopP = 0.9f, TopK = 40 };

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], options);

        inner.LastOptions!.Temperature.Should().BeNull();
        inner.LastOptions.TopP.Should().BeNull();
        inner.LastOptions.TopK.Should().BeNull();
    }

    [Fact]
    public async Task GetResponseAsync_strips_sampling_parameters_even_with_no_raw_factory()
    {
        // Guards against a fast path keyed only on RawRepresentationFactory: an agent with no
        // ReasoningEffort still sets a Temperature, and would still 400.
        RecordingChatClient inner = new();
        AnthropicChatOptionsAdapter client = new(inner);
        ChatOptions options = new() { Temperature = 0.3f };

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], options);

        inner.LastOptions.Should().NotBeSameAs(options);
        inner.LastOptions!.Temperature.Should().BeNull();
        options.Temperature.Should().Be(0.3f);
    }

    [Fact]
    public async Task GetResponseAsync_preserves_the_options_Claude_does_accept()
    {
        RecordingChatClient inner = new();
        AnthropicChatOptionsAdapter client = new(inner);
        ChatOptions options = new()
        {
            Temperature = 0.7f,
            MaxOutputTokens = 1234,
            ModelId = "claude-opus-5",
            RawRepresentationFactory = _ => new object()
        };

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], options);

        inner.LastOptions!.MaxOutputTokens.Should().Be(1234);
        inner.LastOptions.ModelId.Should().Be("claude-opus-5");
    }

    [Fact]
    public async Task GetResponseAsync_passes_options_through_untouched_when_there_is_nothing_to_strip()
    {
        // The common case must not clone: cloning would be pure overhead on every single call.
        RecordingChatClient inner = new();
        AnthropicChatOptionsAdapter client = new(inner);
        ChatOptions options = new() { MaxOutputTokens = 512, ModelId = "claude-opus-5" };

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], options);

        inner.LastOptions.Should().BeSameAs(options);
    }

    [Fact]
    public async Task GetResponseAsync_tolerates_null_options()
    {
        RecordingChatClient inner = new();
        AnthropicChatOptionsAdapter client = new(inner);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], options: null);

        inner.LastOptions.Should().BeNull();
    }

    [Fact]
    public async Task GetStreamingResponseAsync_strips_the_raw_representation_factory()
    {
        // The streaming path is the one an agent run actually takes when a caller streams, so it
        // needs the same treatment — an easy half to forget.
        RecordingChatClient inner = new();
        AnthropicChatOptionsAdapter client = new(inner);
        ChatOptions options = new() { RawRepresentationFactory = _ => new object() };

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")], options))
        {
            // drain
        }

        inner.LastOptions.Should().NotBeNull();
        inner.LastOptions!.RawRepresentationFactory.Should().BeNull();
        options.RawRepresentationFactory.Should().NotBeNull();
    }

    [Fact]
    public void Dispose_disposes_the_client_it_was_given_ownership_of()
    {
        // Nothing else will: the Anthropic SDK's own IChatClient wrapper has an empty Dispose (its
        // IL body is a bare `ret`), so the AnthropicClient it wraps is never released unless this
        // adapter does it. CodeQL flagged exactly that as a missing-Dispose leak.
        RecordingChatClient inner = new();
        TrackedDisposable owned = new();
        AnthropicChatOptionsAdapter client = new(inner, owned);

        client.Dispose();

        owned.DisposeCount.Should().Be(1);
    }

    [Fact]
    public void Dispose_is_safe_when_no_ownership_was_transferred()
    {
        RecordingChatClient inner = new();
        AnthropicChatOptionsAdapter client = new(inner);

        Action act = () => client.Dispose();

        act.Should().NotThrow();
    }

    private sealed class TrackedDisposable : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
    }

    /// <summary>
    /// Captures the <see cref="ChatOptions"/> it was handed. Same hand-written-fake approach the
    /// room executor tests use rather than Moq, since <see cref="IChatClient"/> has an async
    /// iterator member that is awkward to express through a mock.
    /// </summary>
    private sealed class RecordingChatClient : IChatClient
    {
        public ChatOptions? LastOptions { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
