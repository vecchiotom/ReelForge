using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Inference;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

public class InferenceProviderResolverTests
{
    [Fact]
    public async Task ResolveAsync_prefers_the_enabled_per_agent_override_over_the_default()
    {
        Guid agentId = Guid.NewGuid();
        InferenceProvider defaultProvider = MakeProvider(isDefault: true, name: "default");
        InferenceProvider overrideProvider = MakeProvider(isDefault: false, name: "override");

        InferenceProviderResolver resolver = CreateResolver(
            providers: new[] { defaultProvider, overrideProvider },
            overrides: new Dictionary<Guid, Guid?> { [agentId] = overrideProvider.Id });

        ResolvedInferenceProvider resolved = await resolver.ResolveAsync(
            AgentType.CodeStructureAnalyzer, agentId, CancellationToken.None);

        resolved.ProviderId.Should().Be(overrideProvider.Id);
        resolved.Name.Should().Be("override");
    }

    [Fact]
    public async Task ResolveAsync_falls_back_to_the_global_default_when_there_is_no_override()
    {
        InferenceProvider defaultProvider = MakeProvider(isDefault: true, name: "default");

        InferenceProviderResolver resolver = CreateResolver(
            providers: new[] { defaultProvider },
            overrides: new Dictionary<Guid, Guid?>());

        ResolvedInferenceProvider resolved = await resolver.ResolveAsync(
            AgentType.CodeStructureAnalyzer, agentDefinitionId: null, CancellationToken.None);

        resolved.ProviderId.Should().Be(defaultProvider.Id);
    }

    [Fact]
    public async Task ResolveAsync_falls_back_to_AzureOpenAI_config_keys_when_no_provider_rows_exist()
    {
        InferenceProviderResolver resolver = CreateResolver(
            providers: Array.Empty<InferenceProvider>(),
            overrides: new Dictionary<Guid, Guid?>(),
            configOverrides: new Dictionary<string, string?>
            {
                ["AzureOpenAI:Endpoint"] = "https://config-fallback.example",
                ["AzureOpenAI:ApiKey"] = "config-key",
                ["AzureOpenAI:DeploymentName"] = "config-deployment"
            });

        ResolvedInferenceProvider resolved = await resolver.ResolveAsync(
            AgentType.CodeStructureAnalyzer, agentDefinitionId: null, CancellationToken.None);

        resolved.ProviderId.Should().BeNull();
        resolved.Endpoint.Should().Be("https://config-fallback.example");
        resolved.ApiKey.Should().Be("config-key");
        resolved.ModelName.Should().Be("config-deployment");
    }

    [Fact]
    public async Task ResolveAsync_ignores_a_disabled_override_and_falls_through_to_the_default()
    {
        // A real store's LoadEnabledAsync filters to IsEnabled providers, so a disabled provider
        // referenced by an override is deliberately absent from the "enabled" list here -
        // mirroring what the resolver actually sees.
        Guid agentId = Guid.NewGuid();
        InferenceProvider defaultProvider = MakeProvider(isDefault: true, name: "default");
        Guid disabledProviderId = Guid.NewGuid();

        InferenceProviderResolver resolver = CreateResolver(
            providers: new[] { defaultProvider },
            overrides: new Dictionary<Guid, Guid?> { [agentId] = disabledProviderId });

        ResolvedInferenceProvider resolved = await resolver.ResolveAsync(
            AgentType.CodeStructureAnalyzer, agentId, CancellationToken.None);

        resolved.ProviderId.Should().Be(defaultProvider.Id);
    }

    // --- R4 regression: the (capability, is_default) split must never let a default row of
    // one capability answer a resolution for the other capability, regardless of which row
    // happens to appear first in the "enabled" list the store returns. ---

    [Fact]
    public async Task ResolveAsync_never_returns_the_transcription_default_when_a_chat_default_also_exists()
    {
        // Deliberately list the Transcription-capability default FIRST. The pre-fix resolver
        // logic was `enabled.FirstOrDefault(p => p.IsDefault)`, which is order-dependent and
        // would have returned this transcription row to the chat path - this ordering is what
        // makes the test genuinely exercise the bug rather than passing either way.
        InferenceProvider transcriptionDefault = MakeProvider(
            isDefault: true, name: "whisper-default", capability: InferenceProviderCapability.Transcription);
        InferenceProvider chatDefault = MakeProvider(
            isDefault: true, name: "chat-default", capability: InferenceProviderCapability.Chat);

        InferenceProviderResolver resolver = CreateResolver(
            providers: new[] { transcriptionDefault, chatDefault },
            overrides: new Dictionary<Guid, Guid?>());

        ResolvedInferenceProvider resolved = await resolver.ResolveAsync(
            AgentType.CodeStructureAnalyzer, agentDefinitionId: null, CancellationToken.None);

        resolved.ProviderId.Should().Be(chatDefault.Id);
        resolved.Name.Should().Be("chat-default");
    }

    [Fact]
    public async Task ResolveTranscriptionAsync_never_returns_the_chat_default_when_a_transcription_default_also_exists()
    {
        // Reverse of the above: list the Chat-capability default FIRST, so a naive
        // "first IsDefault row" lookup on the transcription path would wrongly return it.
        InferenceProvider chatDefault = MakeProvider(
            isDefault: true, name: "chat-default", capability: InferenceProviderCapability.Chat);
        InferenceProvider transcriptionDefault = MakeProvider(
            isDefault: true, name: "whisper-default", capability: InferenceProviderCapability.Transcription);

        InferenceProviderResolver resolver = CreateResolver(
            providers: new[] { chatDefault, transcriptionDefault },
            overrides: new Dictionary<Guid, Guid?>());

        ResolvedTranscriptionProvider? resolved = await resolver.ResolveTranscriptionAsync(
            explicitProviderId: null, CancellationToken.None);

        resolved.Should().NotBeNull();
        resolved!.ProviderId.Should().Be(transcriptionDefault.Id);
        resolved.Name.Should().Be("whisper-default");
    }

    [Fact]
    public async Task ResolveTranscriptionAsync_prefers_the_explicit_provider_id_over_the_default()
    {
        InferenceProvider defaultProvider = MakeProvider(
            isDefault: true, name: "whisper-default", capability: InferenceProviderCapability.Transcription);
        InferenceProvider explicitProvider = MakeProvider(
            isDefault: false, name: "whisper-explicit", capability: InferenceProviderCapability.Transcription);

        InferenceProviderResolver resolver = CreateResolver(
            providers: new[] { defaultProvider, explicitProvider },
            overrides: new Dictionary<Guid, Guid?>());

        ResolvedTranscriptionProvider? resolved = await resolver.ResolveTranscriptionAsync(
            explicitProviderId: explicitProvider.Id, CancellationToken.None);

        resolved.Should().NotBeNull();
        resolved!.ProviderId.Should().Be(explicitProvider.Id);
        resolved.Name.Should().Be("whisper-explicit");
    }

    [Fact]
    public async Task ResolveTranscriptionAsync_falls_back_to_the_default_when_the_explicit_id_is_not_found()
    {
        InferenceProvider defaultProvider = MakeProvider(
            isDefault: true, name: "whisper-default", capability: InferenceProviderCapability.Transcription);

        InferenceProviderResolver resolver = CreateResolver(
            providers: new[] { defaultProvider },
            overrides: new Dictionary<Guid, Guid?>());

        ResolvedTranscriptionProvider? resolved = await resolver.ResolveTranscriptionAsync(
            explicitProviderId: Guid.NewGuid(), CancellationToken.None);

        resolved.Should().NotBeNull();
        resolved!.ProviderId.Should().Be(defaultProvider.Id);
    }

    [Fact]
    public async Task ResolveTranscriptionAsync_returns_null_when_nothing_resolves_even_with_config_fallback_available()
    {
        // Unlike ResolveAsync (chat), there is deliberately NO fallback to the legacy
        // AzureOpenAI:* configuration keys for transcription - those name a chat deployment, and
        // silently sending audio there would 404 confusingly. Prove this even when the config
        // keys ARE present and a chat default row exists, so a future "just reuse the chat
        // fallback" regression would fail this test.
        InferenceProvider chatDefault = MakeProvider(
            isDefault: true, name: "chat-default", capability: InferenceProviderCapability.Chat);

        InferenceProviderResolver resolver = CreateResolver(
            providers: new[] { chatDefault },
            overrides: new Dictionary<Guid, Guid?>(),
            configOverrides: new Dictionary<string, string?>
            {
                ["AzureOpenAI:Endpoint"] = "https://config-fallback.example",
                ["AzureOpenAI:ApiKey"] = "config-key",
                ["AzureOpenAI:DeploymentName"] = "config-deployment"
            });

        ResolvedTranscriptionProvider? resolved = await resolver.ResolveTranscriptionAsync(
            explicitProviderId: null, CancellationToken.None);

        resolved.Should().BeNull();
    }

    private static InferenceProvider MakeProvider(
        bool isDefault,
        string name,
        InferenceProviderCapability capability = InferenceProviderCapability.Chat) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Kind = InferenceProviderKind.AzureOpenAI,
        Endpoint = "https://provider.example",
        ModelName = "gpt-4o-mini",
        IsDefault = isDefault,
        IsEnabled = true,
        Capability = capability
    };

    private static InferenceProviderResolver CreateResolver(
        IReadOnlyList<InferenceProvider> providers,
        IReadOnlyDictionary<Guid, Guid?> overrides,
        IDictionary<string, string?>? configOverrides = null)
    {
        ServiceCollection services = new();
        services.AddSingleton<IInferenceProviderStore>(new FakeInferenceProviderStore(providers, overrides));
        ServiceProvider serviceProvider = services.BuildServiceProvider();

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configOverrides ?? new Dictionary<string, string?>())
            .Build();

        return new InferenceProviderResolver(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            new PassthroughSecretProtector(),
            NullLogger<InferenceProviderResolver>.Instance);
    }

    private sealed class FakeInferenceProviderStore : IInferenceProviderStore
    {
        private readonly IReadOnlyList<InferenceProvider> _providers;
        private readonly IReadOnlyDictionary<Guid, Guid?> _overrides;

        public FakeInferenceProviderStore(IReadOnlyList<InferenceProvider> providers, IReadOnlyDictionary<Guid, Guid?> overrides)
        {
            _providers = providers;
            _overrides = overrides;
        }

        public Task<IReadOnlyList<InferenceProvider>> LoadEnabledAsync(CancellationToken ct) =>
            Task.FromResult(_providers);

        public Task<IReadOnlyDictionary<Guid, Guid?>> LoadAgentOverridesAsync(CancellationToken ct) =>
            Task.FromResult(_overrides);
    }

    private sealed class PassthroughSecretProtector : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;

        public bool TryUnprotect(string? ciphertext, out string plaintext)
        {
            plaintext = ciphertext ?? string.Empty;
            return ciphertext != null;
        }
    }
}
