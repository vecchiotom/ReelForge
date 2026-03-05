using Microsoft.Agents.AI;
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
    AIAgent AIAgent { get; }
    Task<string> RunAsync(string prompt, CancellationToken ct = default);
}
