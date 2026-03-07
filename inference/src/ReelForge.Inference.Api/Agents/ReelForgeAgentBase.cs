using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ReelForge.Shared.Data.Models;

namespace ReelForge.Inference.Api.Agents;

/// <summary>
/// Abstract base class for ReelForge agents in the API service.
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

    public async Task<AgentRunResult> RunAsync(string prompt, CancellationToken ct = default)
    {
        AgentResponse agentResponse = await _aiAgent.RunAsync(prompt, cancellationToken: ct);
        var chatResponse = agentResponse.AsChatResponse();
        string output = chatResponse.Text ?? string.Empty;

        // Extract token usage from the response
        int totalTokens = 0;
        int? inputTokens = null;
        int? outputTokens = null;

        if (chatResponse.Usage != null)
        {
            inputTokens = (int?)(chatResponse.Usage.InputTokenCount ?? 0);
            outputTokens = (int?)(chatResponse.Usage.OutputTokenCount ?? 0);
            totalTokens = (int)(chatResponse.Usage.TotalTokenCount ??
                          ((inputTokens ?? 0) + (outputTokens ?? 0)));
        }

        return new AgentRunResult
        {
            Output = output,
            TokensUsed = totalTokens,
            InputTokens = inputTokens,
            OutputTokens = outputTokens
        };
    }
}
