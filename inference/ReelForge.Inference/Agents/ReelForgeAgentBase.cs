using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ReelForge.Inference.Data.Models;

namespace ReelForge.Inference.Agents;

/// <summary>
/// Abstract base class for all ReelForge agents.
/// </summary>
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

        // Read system prompt from config, fallback to default
        string configKey = $"Agents:{name}:SystemPrompt";
        SystemPrompt = configuration[configKey] ?? defaultSystemPrompt;

        _aiAgent = chatClient.AsAIAgent(
            instructions: SystemPrompt,
            name: name,
            tools: _tools.Cast<AITool>().ToList());
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Description { get; }

    /// <inheritdoc />
    public string SystemPrompt { get; }

    /// <inheritdoc />
    public AgentType AgentType { get; }

    /// <inheritdoc />
    public IReadOnlyList<AIFunction> Tools => _tools.AsReadOnly();

    /// <inheritdoc />
    public AIAgent AIAgent => _aiAgent;

    /// <inheritdoc />
    public async Task<string> RunAsync(string prompt, CancellationToken ct = default)
    {
        AgentResponse result = await _aiAgent.RunAsync(prompt);
        return result.AsChatResponse().Text ?? string.Empty;
    }
}
