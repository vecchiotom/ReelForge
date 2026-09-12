using FluentAssertions;
using Microsoft.Extensions.AI;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Inference;
using System;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

public class ChatClientFactoryTests
{
    [Fact]
    public void Get_constructs_an_AzureOpenAI_client_without_throwing()
    {
        ChatClientFactory factory = new();
        ResolvedInferenceProvider provider = MakeProvider(InferenceProviderKind.AzureOpenAI, apiKey: "some-key");

        Action act = () => factory.Get(provider);

        act.Should().NotThrow();
    }

    [Fact]
    public void Get_constructs_an_OpenAICompatible_client_without_throwing_when_api_key_is_empty()
    {
        // Regression test for Risk R5: System.ClientModel.ApiKeyCredential throws on a null or
        // empty string, but self-hosted OpenAI-compatible deployments (vLLM in particular)
        // commonly run with no key configured at all.
        ChatClientFactory factory = new();
        ResolvedInferenceProvider provider = MakeProvider(InferenceProviderKind.OpenAICompatible, apiKey: string.Empty);

        Action act = () => factory.Get(provider);

        act.Should().NotThrow();
    }

    [Fact]
    public void Get_constructs_an_OpenAICompatible_client_without_throwing_when_api_key_is_whitespace()
    {
        ChatClientFactory factory = new();
        ResolvedInferenceProvider provider = MakeProvider(InferenceProviderKind.OpenAICompatible, apiKey: "   ");

        Action act = () => factory.Get(provider);

        act.Should().NotThrow();
    }

    [Fact]
    public void Get_returns_the_same_cached_instance_for_identical_configuration()
    {
        ChatClientFactory factory = new();
        ResolvedInferenceProvider provider1 = MakeProvider(InferenceProviderKind.AzureOpenAI, apiKey: "key", modelName: "gpt-4o-mini");
        ResolvedInferenceProvider provider2 = MakeProvider(InferenceProviderKind.AzureOpenAI, apiKey: "key", modelName: "gpt-4o-mini");

        IChatClient client1 = factory.Get(provider1);
        IChatClient client2 = factory.Get(provider2);

        client1.Should().BeSameAs(client2);
    }

    [Fact]
    public void Get_returns_a_different_instance_when_only_the_model_name_changes()
    {
        ChatClientFactory factory = new();
        ResolvedInferenceProvider provider1 = MakeProvider(InferenceProviderKind.AzureOpenAI, apiKey: "key", modelName: "gpt-4o-mini");
        ResolvedInferenceProvider provider2 = MakeProvider(InferenceProviderKind.AzureOpenAI, apiKey: "key", modelName: "gpt-4o");

        IChatClient client1 = factory.Get(provider1);
        IChatClient client2 = factory.Get(provider2);

        client1.Should().NotBeSameAs(client2);
        provider1.CacheKey.Should().NotBe(provider2.CacheKey);
    }

    [Fact]
    public void CacheKey_never_contains_the_raw_api_key()
    {
        const string secret = "super-secret-api-key-value";
        ResolvedInferenceProvider provider = MakeProvider(InferenceProviderKind.AzureOpenAI, apiKey: secret);

        provider.CacheKey.Should().NotContain(secret);
    }

    private static ResolvedInferenceProvider MakeProvider(
        InferenceProviderKind kind,
        string apiKey,
        string modelName = "gpt-4o-mini") => new(
            ProviderId: Guid.NewGuid(),
            Name: "test-provider",
            Kind: kind,
            Endpoint: "https://example.invalid",
            ModelName: modelName,
            ApiKey: apiKey,
            TimeoutSeconds: 300);
}
