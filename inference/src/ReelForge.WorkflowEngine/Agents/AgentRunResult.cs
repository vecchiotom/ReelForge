namespace ReelForge.WorkflowEngine.Agents;

/// <summary>
/// Result from running an agent, including output and token usage metadata.
/// </summary>
public class AgentRunResult
{
    public required string Output { get; init; }
    public int TokensUsed { get; init; }
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public IReadOnlyList<AgentToolCallTrace> ToolCalls { get; init; } = [];
    public IReadOnlyList<string> Reasoning { get; init; } = [];
    public bool Success { get; init; } = true;
    public string? FailureReason { get; init; }
}

public class AgentToolCallTrace
{
    public required string ToolName { get; init; }
    public string? Arguments { get; init; }
    public string? Result { get; init; }
}
