using Microsoft.Extensions.AI;
using ReelForge.Shared.Data.Models;

namespace ReelForge.Shared.Inference;

public sealed class AgentChatClientProvider : IAgentChatClientProvider
{
    private readonly IInferenceProviderResolver _resolver;
    private readonly IChatClientFactory _factory;

    public AgentChatClientProvider(IInferenceProviderResolver resolver, IChatClientFactory factory)
    {
        _resolver = resolver;
        _factory = factory;
    }

    public async ValueTask<IChatClient> GetAsync(AgentType agentType, Guid? agentDefinitionId, CancellationToken ct)
    {
        ResolvedInferenceProvider provider = await _resolver.ResolveAsync(agentType, agentDefinitionId, ct);
        return _factory.Get(provider);
    }
}
