using ReelForge.Shared.Data.Models;

namespace ReelForge.WorkflowEngine.Agents;

public interface IAgentRegistry
{
    IReelForgeAgent? GetByType(AgentType agentType);
    IReadOnlyList<IReelForgeAgent> GetAll();
}
