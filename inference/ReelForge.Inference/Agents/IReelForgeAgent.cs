using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ReelForge.Inference.Data.Models;

namespace ReelForge.Inference.Agents;

/// <summary>
/// Interface for all ReelForge agents.
/// </summary>
public interface IReelForgeAgent
{
    /// <summary>Gets the agent's display name.</summary>
    string Name { get; }

    /// <summary>Gets a description of what the agent does.</summary>
    string Description { get; }

    /// <summary>Gets the system prompt for the agent.</summary>
    string SystemPrompt { get; }

    /// <summary>Gets the agent type enum value.</summary>
    AgentType AgentType { get; }

    /// <summary>Gets the tools available to this agent.</summary>
    IReadOnlyList<AIFunction> Tools { get; }

    /// <summary>Gets the underlying AI agent instance.</summary>
    AIAgent AIAgent { get; }

    /// <summary>Runs the agent with the given prompt and returns the response.</summary>
    Task<string> RunAsync(string prompt, CancellationToken ct = default);
}
