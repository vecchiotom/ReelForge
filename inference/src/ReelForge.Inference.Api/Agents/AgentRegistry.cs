using ReelForge.Shared.Data.Models;

namespace ReelForge.Inference.Api.Agents;

/// <summary>
/// Agent registry for the API service (file summarization only).
/// </summary>
public class AgentRegistry : IAgentRegistry
{
    private readonly Dictionary<AgentType, IReelForgeAgent> _agents;
    private readonly List<IReelForgeAgent> _allAgents;

    public AgentRegistry(IEnumerable<IReelForgeAgent> agents)
    {
        _allAgents = agents.ToList();
        _agents = new Dictionary<AgentType, IReelForgeAgent>();
        foreach (IReelForgeAgent agent in _allAgents)
        {
            if (agent.AgentType != AgentType.Custom && agent.OutputSchemaType == null)
            {
                throw new InvalidOperationException(
                    $"Built-in agent '{agent.Name}' ({agent.AgentType}) must declare an output schema type.");
            }

            _agents.TryAdd(agent.AgentType, agent);
        }
    }

    public IReelForgeAgent? GetByType(AgentType agentType)
    {
        _agents.TryGetValue(agentType, out IReelForgeAgent? agent);
        return agent;
    }

    public IReadOnlyList<IReelForgeAgent> GetAll() => _allAgents.AsReadOnly();
}
