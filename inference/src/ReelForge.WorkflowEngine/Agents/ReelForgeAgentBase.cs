using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ReelForge.Shared.Data.Models;

namespace ReelForge.WorkflowEngine.Agents;

public abstract class ReelForgeAgentBase : IReelForgeAgent
{
    private readonly IChatClient _chatClient;
    private readonly List<AIFunction> _tools;

    protected ReelForgeAgentBase(
        IChatClient chatClient,
        IConfiguration configuration,
        string name,
        string description,
        AgentType agentType,
        string defaultSystemPrompt,
        IEnumerable<AIFunction>? tools = null,
        Guid? agentId = null)
    {
        _chatClient = chatClient;
        Name = name;
        Description = description;
        AgentType = agentType;
        AgentId = agentId;
        _tools = tools?.ToList() ?? new List<AIFunction>();

        string configKey = $"Agents:{name}:SystemPrompt";
        SystemPrompt = configuration[configKey] ?? defaultSystemPrompt;
    }

    public Guid? AgentId { get; }
    public string Name { get; }
    public string Description { get; }
    public string SystemPrompt { get; }
    public AgentType AgentType { get; }
    public IReadOnlyList<AIFunction> Tools => _tools.AsReadOnly();
    public AIAgent AIAgent => CreateAgent();

    public async Task<string> RunAsync(string prompt, CancellationToken ct = default)
    {
        AIAgent agent = CreateAgent();
        AgentResponse result = await agent.RunAsync(prompt);
        return result.AsChatResponse().Text ?? string.Empty;
    }

    private AIAgent CreateAgent() =>
        _chatClient.AsAIAgent(
            instructions: SystemPrompt,
            name: Name,
            tools: _tools.Cast<AITool>().ToList());
}
