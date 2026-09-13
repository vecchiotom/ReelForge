using Microsoft.Extensions.AI;
using ReelForge.Shared.Data.Models;

namespace ReelForge.WorkflowEngine.Agents;

public interface IReelForgeAgent
{
    Guid? AgentId { get; }
    string Name { get; }
    string Description { get; }
    string SystemPrompt { get; }
    AgentType AgentType { get; }
    IReadOnlyList<AIFunction> Tools { get; }
    string? OutputSchemaJson { get; }
    Type? OutputSchemaType { get; }
    /// <param name="agentDefinitionId">
    /// The concrete WorkflowStep.AgentDefinitionId driving this run, when known to the caller.
    /// Passed through to IAgentChatClientProvider so a per-agent InferenceProvider override
    /// actually applies — without it, provider resolution falls back to this agent's
    /// constructor-time AgentId (usually null), and a per-definition override can never be
    /// selected (found by Copilot review).
    /// </param>
    Task<AgentRunResult> RunAsync(string prompt, Guid? agentDefinitionId = null, CancellationToken ct = default);
}
