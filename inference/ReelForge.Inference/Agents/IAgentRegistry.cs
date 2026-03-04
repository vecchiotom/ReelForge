using ReelForge.Inference.Data.Models;

namespace ReelForge.Inference.Agents;

/// <summary>
/// Registry for resolving agents by type or definition ID.
/// </summary>
public interface IAgentRegistry
{
    /// <summary>Resolves a built-in agent by its agent type.</summary>
    IReelForgeAgent? GetByType(AgentType agentType);

    /// <summary>Gets all registered built-in agents.</summary>
    IReadOnlyList<IReelForgeAgent> GetAll();
}
