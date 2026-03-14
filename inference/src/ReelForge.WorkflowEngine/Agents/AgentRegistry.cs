using ReelForge.Shared.Data.Models;

namespace ReelForge.WorkflowEngine.Agents;

public class AgentRegistry : IAgentRegistry
{
    private readonly Dictionary<AgentType, IReelForgeAgent> _agents;
    private readonly Dictionary<Guid, IReelForgeAgent> _customAgentsById;
    private readonly List<IReelForgeAgent> _allAgents;

    public AgentRegistry(IEnumerable<IReelForgeAgent> agents)
    {
        _allAgents = agents.ToList();
        _agents = new Dictionary<AgentType, IReelForgeAgent>();
        _customAgentsById = new Dictionary<Guid, IReelForgeAgent>();
        foreach (IReelForgeAgent agent in _allAgents)
        {
            if (agent.AgentType != AgentType.Custom && agent.OutputSchemaType == null)
            {
                throw new InvalidOperationException(
                    $"Built-in agent '{agent.Name}' ({agent.AgentType}) must declare an output schema type.");
            }

            _agents.TryAdd(agent.AgentType, agent);

            if (agent.AgentType == AgentType.Custom && agent.AgentId.HasValue)
            {
                _customAgentsById[agent.AgentId.Value] = agent;
            }
        }
    }

    public IReelForgeAgent? GetByType(AgentType agentType, Guid? agentId = null)
    {
        if (agentType == AgentType.Custom)
        {
            if (!agentId.HasValue)
            {
                return null;
            }

            _customAgentsById.TryGetValue(agentId.Value, out IReelForgeAgent? customAgent);
            return customAgent;
        }

        _agents.TryGetValue(agentType, out IReelForgeAgent? agent);
        return agent;
    }

    public IReadOnlyList<IReelForgeAgent> GetAll() => _allAgents.AsReadOnly();
}
