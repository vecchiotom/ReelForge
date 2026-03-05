using ReelForge.Shared.Data.Models;

namespace ReelForge.Inference.Api.Agents;

/// <summary>
/// Registry for resolving agents by type (API-side, file summarization only).
/// </summary>
public interface IAgentRegistry
{
    IReelForgeAgent? GetByType(AgentType agentType);
    IReadOnlyList<IReelForgeAgent> GetAll();
}
