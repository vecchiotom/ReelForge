using ReelForge.Shared.Data.Models;

namespace ReelForge.Shared.Inference;

/// <summary>
/// Resolves the inference provider a given agent run should use, with precedence:
/// per-agent override (if that provider is enabled) &gt; global default provider
/// (<c>IsDefault &amp;&amp; IsEnabled &amp;&amp; Capability == Chat</c>) &gt; the legacy
/// <c>AzureOpenAI:*</c> configuration keys. The configuration fallback keeps every existing
/// `.env`-only deployment working with zero rows in <c>inference_providers</c>.
/// </summary>
public interface IInferenceProviderResolver
{
    Task<ResolvedInferenceProvider> ResolveAsync(AgentType agentType, Guid? agentDefinitionId, CancellationToken ct);

    /// <summary>
    /// Resolves the transcription provider a video-analyze step should use, with precedence:
    /// <paramref name="explicitProviderId"/> (if given and that provider is enabled) &gt; the
    /// single enabled row with <c>Capability == Transcription &amp;&amp; IsDefault</c> &gt;
    /// <c>null</c>. Deliberately no fallback to the legacy <c>AzureOpenAI:*</c> configuration
    /// keys — those name a chat deployment, and silently sending audio there would 404
    /// confusingly. Returns <c>null</c> when nothing resolves, so callers (<c>Optional</c> mode)
    /// can degrade cleanly instead of throwing.
    /// </summary>
    Task<ResolvedTranscriptionProvider?> ResolveTranscriptionAsync(Guid? explicitProviderId, CancellationToken ct);

    /// <summary>
    /// Resolves the vision (image-captioning) provider a video-analyze step's shot-captioning
    /// stage should use, with precedence: <paramref name="explicitProviderId"/> (if given and
    /// that provider is enabled) &gt; the single enabled row with
    /// <c>Capability == Vision &amp;&amp; IsDefault</c> &gt; <c>null</c>. Returns
    /// <see cref="ResolvedInferenceProvider"/> (not a dedicated vision type) because a vision call
    /// reuses the same chat-completions machinery <see cref="ResolveAsync"/> does — an image is
    /// just another content part on an otherwise ordinary chat message — so
    /// <see cref="ResolvedInferenceProvider"/>/<c>IChatClientFactory</c> need no vision-specific
    /// variant. Deliberately no fallback to the legacy <c>AzureOpenAI:*</c> configuration keys,
    /// same rationale as <see cref="ResolveTranscriptionAsync"/>: silently sending an image to a
    /// deployment that may not support vision would fail confusingly. Returns <c>null</c> when
    /// nothing resolves, so callers (<c>VideoVisionMode.Optional</c>) can degrade cleanly instead
    /// of throwing.
    /// </summary>
    Task<ResolvedInferenceProvider?> ResolveVisionAsync(Guid? explicitProviderId, CancellationToken ct);
}
