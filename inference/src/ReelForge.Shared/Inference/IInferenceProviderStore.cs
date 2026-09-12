using ReelForge.Shared.Data.Models;

namespace ReelForge.Shared.Inference;

/// <summary>
/// Reads configured inference providers and per-agent overrides. Implemented once per service
/// over that service's own DbContext (<c>inference_providers</c> and <c>agent_definitions</c>
/// are owned by the Inference API; the WorkflowEngine's implementation reads the same tables
/// through its read-only, excluded-from-migrations mapping).
/// </summary>
public interface IInferenceProviderStore
{
    Task<IReadOnlyList<InferenceProvider>> LoadEnabledAsync(CancellationToken ct);

    /// <summary>Maps agentDefinitionId -&gt; inferenceProviderId (possibly null) for every agent definition.</summary>
    Task<IReadOnlyDictionary<Guid, Guid?>> LoadAgentOverridesAsync(CancellationToken ct);
}
