using Microsoft.Extensions.AI;
using ReelForge.Shared.Data.Models;

namespace ReelForge.Shared.Inference;

/// <summary>
/// The single seam agents depend on to obtain an <see cref="IChatClient"/> for a given run —
/// a trivial composition of <see cref="IInferenceProviderResolver"/> and <see cref="IChatClientFactory"/>.
/// </summary>
public interface IAgentChatClientProvider
{
    ValueTask<IChatClient> GetAsync(AgentType agentType, Guid? agentDefinitionId, CancellationToken ct);
}
