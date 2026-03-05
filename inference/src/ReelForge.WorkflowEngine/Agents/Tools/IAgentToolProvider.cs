using Microsoft.Extensions.AI;
using ReelForge.Shared.Data.Models;

namespace ReelForge.WorkflowEngine.Agents.Tools;

public interface IAgentToolProvider
{
    IReadOnlyList<AIFunction> GetTools(AgentType agentType);
}
