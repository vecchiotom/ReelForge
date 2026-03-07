namespace ReelForge.Inference.Api.Agents;

/// <summary>
/// Result from running an agent, including output and token usage metadata.
/// </summary>
public class AgentRunResult
{
    public required string Output { get; init; }
    public int TokensUsed { get; init; }
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
}
