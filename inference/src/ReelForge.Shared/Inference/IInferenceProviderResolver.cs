using ReelForge.Shared.Data.Models;

namespace ReelForge.Shared.Inference;

/// <summary>
/// Resolves the inference provider a given agent run should use, with precedence:
/// per-agent override (if that provider is enabled) &gt; global default provider
/// (<c>IsDefault &amp;&amp; IsEnabled</c>) &gt; the legacy <c>AzureOpenAI:*</c> configuration keys.
/// The configuration fallback keeps every existing `.env`-only deployment working with zero
/// rows in <c>inference_providers</c>.
/// </summary>
public interface IInferenceProviderResolver
{
    Task<ResolvedInferenceProvider> ResolveAsync(AgentType agentType, Guid? agentDefinitionId, CancellationToken ct);
}
