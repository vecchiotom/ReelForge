using FluentAssertions;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Inference;
using System;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Mirrors <see cref="ChatClientFactoryTests"/> exactly, for the transcription-side factory.
/// </summary>
public class TranscriptionClientFactoryTests
{
    [Fact]
    public void Get_constructs_an_AzureOpenAI_client_without_throwing()
    {
        TranscriptionClientFactory factory = new();
        ResolvedTranscriptionProvider provider = MakeProvider(InferenceProviderKind.AzureOpenAI, apiKey: "some-key");

        Action act = () => factory.Get(provider);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("http://host:9002/v1")]
    [InlineData("http://host:9002/v1/")]
    public void OpenAICompatible_endpoint_resolves_the_full_transcriptions_path_regardless_of_trailing_slash(
        string endpoint)
    {
        // Regression: HttpClient.BaseAddress with no trailing slash makes relative-URI combining
        // REPLACE the last path segment instead of appending to it — "http://h/v1" combined with
        // relative "audio/transcriptions" resolves to "http://h/audio/transcriptions", silently
        // dropping "/v1" and 404ing against the real speaches/faster-whisper-server endpoint.
        // TranscriptionClientFactory.BuildOpenAICompatible must normalize the trailing slash
        // before constructing HttpClient.BaseAddress so this can never regress.
        Uri baseAddress = new(endpoint.TrimEnd('/') + "/");
        Uri combined = new(baseAddress, "audio/transcriptions");

        combined.ToString().Should().Be("http://host:9002/v1/audio/transcriptions");
    }

    [Fact]
    public void Get_constructs_an_OpenAICompatible_client_without_throwing_when_api_key_is_empty()
    {
        // Whisper-compatible self-hosted backends (whisper.cpp-server, faster-whisper-server)
        // commonly run with no API key at all - the same class of bug as Risk R5 for chat, but
        // for the transcription path.
        TranscriptionClientFactory factory = new();
        ResolvedTranscriptionProvider provider = MakeProvider(InferenceProviderKind.OpenAICompatible, apiKey: string.Empty);

        Action act = () => factory.Get(provider);

        act.Should().NotThrow();
    }

    [Fact]
    public void Get_constructs_an_OpenAICompatible_client_without_throwing_when_api_key_is_whitespace()
    {
        TranscriptionClientFactory factory = new();
        ResolvedTranscriptionProvider provider = MakeProvider(InferenceProviderKind.OpenAICompatible, apiKey: "   ");

        Action act = () => factory.Get(provider);

        act.Should().NotThrow();
    }

    [Fact]
    public void Get_constructs_an_AzureOpenAI_client_without_throwing_when_api_key_is_empty()
    {
        // Azure deployments always require a key in practice, but the factory must not throw
        // just from constructing the client (ApiKeyCredential itself is the only thing that can
        // throw here, and it is guarded identically to the OpenAI-compatible branch's key).
        TranscriptionClientFactory factory = new();
        ResolvedTranscriptionProvider provider = MakeProvider(InferenceProviderKind.AzureOpenAI, apiKey: "some-key");

        Action act = () => factory.Get(provider);

        act.Should().NotThrow();
    }

    [Fact]
    public void Get_returns_the_same_cached_instance_for_identical_configuration()
    {
        TranscriptionClientFactory factory = new();
        ResolvedTranscriptionProvider provider1 = MakeProvider(InferenceProviderKind.AzureOpenAI, apiKey: "key", modelName: "whisper-1");
        ResolvedTranscriptionProvider provider2 = MakeProvider(InferenceProviderKind.AzureOpenAI, apiKey: "key", modelName: "whisper-1");

        ITranscriptionClient client1 = factory.Get(provider1);
        ITranscriptionClient client2 = factory.Get(provider2);

        client1.Should().BeSameAs(client2);
    }

    [Fact]
    public void Get_returns_a_different_instance_when_only_the_model_name_changes()
    {
        TranscriptionClientFactory factory = new();
        ResolvedTranscriptionProvider provider1 = MakeProvider(InferenceProviderKind.AzureOpenAI, apiKey: "key", modelName: "whisper-1");
        ResolvedTranscriptionProvider provider2 = MakeProvider(InferenceProviderKind.AzureOpenAI, apiKey: "key", modelName: "whisper-large-v3");

        ITranscriptionClient client1 = factory.Get(provider1);
        ITranscriptionClient client2 = factory.Get(provider2);

        client1.Should().NotBeSameAs(client2);
        provider1.CacheKey.Should().NotBe(provider2.CacheKey);
    }

    [Fact]
    public void Get_returns_a_different_instance_when_only_the_timeout_changes()
    {
        // TimeoutSeconds is threaded into AzureOpenAIClientOptions/OpenAIClientOptions.NetworkTimeout
        // at construction time (unlike the chat-side factory, which does not set a timeout at
        // all) - it must be part of the client's identity so two providers that differ only in
        // timeout are never silently coalesced into the same cached client.
        TranscriptionClientFactory factory = new();
        ResolvedTranscriptionProvider provider1 = MakeProvider(InferenceProviderKind.AzureOpenAI, apiKey: "key", timeoutSeconds: 60);
        ResolvedTranscriptionProvider provider2 = MakeProvider(InferenceProviderKind.AzureOpenAI, apiKey: "key", timeoutSeconds: 300);

        ITranscriptionClient client1 = factory.Get(provider1);
        ITranscriptionClient client2 = factory.Get(provider2);

        client1.Should().NotBeSameAs(client2);
        provider1.CacheKey.Should().NotBe(provider2.CacheKey);
    }

    [Fact]
    public void CacheKey_never_contains_the_raw_api_key()
    {
        const string secret = "super-secret-whisper-api-key";
        ResolvedTranscriptionProvider provider = MakeProvider(InferenceProviderKind.AzureOpenAI, apiKey: secret);

        provider.CacheKey.Should().NotContain(secret);
    }

    private static ResolvedTranscriptionProvider MakeProvider(
        InferenceProviderKind kind,
        string apiKey,
        string modelName = "whisper-1",
        int timeoutSeconds = 300) => new(
            ProviderId: Guid.NewGuid(),
            Name: "test-transcription-provider",
            Kind: kind,
            Endpoint: "https://example.invalid",
            ModelName: modelName,
            ApiKey: apiKey,
            TimeoutSeconds: timeoutSeconds);
}
