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
    public void Get_constructs_an_Anthropic_client_without_throwing()
    {
        ChatClientFactory factory = new();
        ResolvedInferenceProvider provider = MakeProvider(
            InferenceProviderKind.Anthropic, apiKey: "sk-ant-api03-example", modelName: "claude-opus-5");

        Action act = () => factory.Get(provider);

        act.Should().NotThrow();
    }

    [Fact]
    public void Get_constructs_an_Anthropic_client_without_throwing_when_the_key_is_an_oauth_token()
    {
        // An `sk-ant-oat...` value must travel as `Authorization: Bearer`, not `x-api-key`. The
        // factory branches on the prefix; this asserts the branch is reachable and constructs.
        ChatClientFactory factory = new();
        ResolvedInferenceProvider provider = MakeProvider(
            InferenceProviderKind.Anthropic, apiKey: "sk-ant-oat01-example", modelName: "claude-opus-5");

        Action act = () => factory.Get(provider);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Get_throws_for_an_Anthropic_provider_with_no_usable_key(string apiKey)
    {
        // An empty key must NOT fall through to the Anthropic SDK's ambient credential resolution.
        // AnthropicClient.ShouldAutoResolveCredentials is get-only and defaults to true, so both
        // properties left null means requests silently go out on whatever ANTHROPIC_API_KEY the
        // container happens to carry. Since executing a workflow requires no admin rights, that
        // would let any authenticated user spend the host's credential — and a Data Protection key
        // ring mismatch (which the resolver degrades to an empty key) would reroute billing rather
        // than failing. Ambient use has to be asked for explicitly instead.
        ChatClientFactory factory = new();
        ResolvedInferenceProvider provider = MakeProvider(
            InferenceProviderKind.Anthropic, apiKey: apiKey, modelName: "claude-opus-5");

        Action act = () => factory.Get(provider);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no usable API key*");
    }

    [Fact]
    public void Get_constructs_an_Anthropic_client_when_ambient_credentials_are_explicitly_requested()
    {
        // The deliberate opt-in: the operator stored the sentinel, so deferring to the SDK's own
        // env/profile resolution is what they asked for.
        ChatClientFactory factory = new();
        ResolvedInferenceProvider provider = MakeProvider(
            InferenceProviderKind.Anthropic,
            apiKey: ChatClientFactory.AnthropicAmbientCredentialSentinel,
            modelName: "claude-opus-5");

        Action act = () => factory.Get(provider);

        act.Should().NotThrow();
    }

    [Fact]
    public void Get_constructs_an_Anthropic_client_without_throwing_when_the_endpoint_is_blank()
    {
        // A blank endpoint means "use the SDK's production default", so it must not reach
        // `new Uri(...)` the way the two OpenAI arms' endpoints do.
        ChatClientFactory factory = new();
        ResolvedInferenceProvider provider = new(
            ProviderId: Guid.NewGuid(),
            Name: "anthropic-no-endpoint",
            Kind: InferenceProviderKind.Anthropic,
            Endpoint: "   ",
            ModelName: "claude-opus-5",
            ApiKey: "sk-ant-api03-example",
            TimeoutSeconds: 300);

        Action act = () => factory.Get(provider);

        act.Should().NotThrow();
    }

    [Fact]
    public void Get_returns_the_same_cached_Anthropic_instance_for_identical_configuration()
    {
        ChatClientFactory factory = new();
        ResolvedInferenceProvider provider1 = MakeProvider(InferenceProviderKind.Anthropic, apiKey: "k", modelName: "claude-opus-5");
        ResolvedInferenceProvider provider2 = MakeProvider(InferenceProviderKind.Anthropic, apiKey: "k", modelName: "claude-opus-5");

        IChatClient client1 = factory.Get(provider1);
        IChatClient client2 = factory.Get(provider2);

        client1.Should().BeSameAs(client2);
    }

    [Fact]
    public void Get_returns_a_different_instance_for_the_same_model_under_a_different_kind()
    {
        // Kind participates in the cache key, so an Anthropic row and an OpenAI-compatible row
        // that happen to name the same model must never share a client.
        ChatClientFactory factory = new();
        ResolvedInferenceProvider anthropic = MakeProvider(InferenceProviderKind.Anthropic, apiKey: "k", modelName: "claude-opus-5");
        ResolvedInferenceProvider compatible = MakeProvider(InferenceProviderKind.OpenAICompatible, apiKey: "k", modelName: "claude-opus-5");

        factory.Get(anthropic).Should().NotBeSameAs(factory.Get(compatible));
        anthropic.CacheKey.Should().NotBe(compatible.CacheKey);
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
