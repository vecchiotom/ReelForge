using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ReelForge.Shared.Data.Models;

namespace ReelForge.WorkflowEngine.Agents;

public abstract class ReelForgeAgentBase : IReelForgeAgent
{
    private readonly AIAgent _aiAgent;
    private readonly List<AIFunction> _tools;

    protected ReelForgeAgentBase(
        IChatClient chatClient,
        IConfiguration configuration,
        string name,
        string description,
        AgentType agentType,
        string defaultSystemPrompt,
        IEnumerable<AIFunction>? tools = null)
    {
        Name = name;
        Description = description;
        AgentType = agentType;
        _tools = tools?.ToList() ?? new List<AIFunction>();

        string configKey = $"Agents:{name}:SystemPrompt";
        SystemPrompt = configuration[configKey] ?? defaultSystemPrompt;

        _aiAgent = chatClient.AsAIAgent(
            instructions: SystemPrompt,
            name: name,
            tools: _tools.Cast<AITool>().ToList());
    }

    public string Name { get; }
    public string Description { get; }
    public string SystemPrompt { get; }
    public AgentType AgentType { get; }
    public IReadOnlyList<AIFunction> Tools => _tools.AsReadOnly();
    public AIAgent AIAgent => _aiAgent;

    public async Task<string> RunAsync(string prompt, CancellationToken ct = default)
    {
        AgentResponse result = await _aiAgent.RunAsync(prompt);
        return result.AsChatResponse().Text ?? string.Empty;
    }
}
