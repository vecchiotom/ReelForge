using ReelForge.Shared.Data.Models;

namespace ReelForge.WorkflowEngine.Agents;

public interface IAgentRegistry
{
    IReelForgeAgent? GetByType(AgentType agentType, Guid? agentId = null);
    IReadOnlyList<IReelForgeAgent> GetAll();
}
