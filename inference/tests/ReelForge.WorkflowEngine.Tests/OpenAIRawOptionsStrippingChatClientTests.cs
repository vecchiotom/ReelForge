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
/// <c>ReelForgeAgentBase.BuildChatOptions</c> attaches an <c>OpenAI.Chat.ChatCompletionOptions</c>
/// via <see cref="ChatOptions.RawRepresentationFactory"/> for every agent configured with a
/// <c>ReasoningEffort</c>, and those options are built once per agent and reused on every run —
/// long before the per-run provider is known. These tests pin the two properties that make routing
/// such an agent to a non-OpenAI backend safe: the raw factory never reaches the inner client, and
/// the caller's own <see cref="ChatOptions"/> instance is never mutated in the process.
/// </summary>
public class OpenAIRawOptionsStrippingChatClientTests
{
    [Fact]
    public async Task GetResponseAsync_strips_the_raw_representation_factory()
    {
        RecordingChatClient inner = new();
        OpenAIRawOptionsStrippingChatClient client = new(inner);
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
        OpenAIRawOptionsStrippingChatClient client = new(inner);
        Func<IChatClient, object?> factory = _ => new object();
        ChatOptions options = new() { RawRepresentationFactory = factory };

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], options);

        options.RawRepresentationFactory.Should().BeSameAs(factory);
        inner.LastOptions.Should().NotBeSameAs(options);
    }

    [Fact]
    public async Task GetResponseAsync_preserves_every_other_option()
    {
        RecordingChatClient inner = new();
        OpenAIRawOptionsStrippingChatClient client = new(inner);
        ChatOptions options = new()
        {
            Temperature = 0.7f,
            TopP = 0.9f,
            TopK = 40,
            MaxOutputTokens = 1234,
            ModelId = "claude-opus-5",
            RawRepresentationFactory = _ => new object()
        };

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], options);

        inner.LastOptions!.Temperature.Should().Be(0.7f);
        inner.LastOptions.TopP.Should().Be(0.9f);
        inner.LastOptions.TopK.Should().Be(40);
        inner.LastOptions.MaxOutputTokens.Should().Be(1234);
        inner.LastOptions.ModelId.Should().Be("claude-opus-5");
    }

    [Fact]
    public async Task GetResponseAsync_passes_options_through_untouched_when_there_is_nothing_to_strip()
    {
        // The common case must not clone: cloning would be pure overhead on every single call.
        RecordingChatClient inner = new();
        OpenAIRawOptionsStrippingChatClient client = new(inner);
        ChatOptions options = new() { Temperature = 0.5f };

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], options);

        inner.LastOptions.Should().BeSameAs(options);
    }

    [Fact]
    public async Task GetResponseAsync_tolerates_null_options()
    {
        RecordingChatClient inner = new();
        OpenAIRawOptionsStrippingChatClient client = new(inner);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], options: null);

        inner.LastOptions.Should().BeNull();
    }

    [Fact]
    public async Task GetStreamingResponseAsync_strips_the_raw_representation_factory()
    {
        // The streaming path is the one an agent run actually takes when a caller streams, so it
        // needs the same treatment — an easy half to forget.
        RecordingChatClient inner = new();
        OpenAIRawOptionsStrippingChatClient client = new(inner);
        ChatOptions options = new() { RawRepresentationFactory = _ => new object() };

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")], options))
        {
            // drain
        }

        inner.LastOptions.Should().NotBeNull();
        inner.LastOptions!.RawRepresentationFactory.Should().BeNull();
        options.RawRepresentationFactory.Should().NotBeNull();
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
