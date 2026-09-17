using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ReelForge.Shared.Data.Models;
using ReelForge.WorkflowEngine.Agents;

namespace ReelForge.WorkflowEngine.Execution.StepExecutors;

/// <summary>
/// Executes a standard agent step - runs the agent and returns its output.
/// </summary>
public class AgentStepExecutor : IStepExecutor
{
    private readonly IAgentRegistry _agentRegistry;
    private readonly IWorkflowExecutionContextAccessor _executionContextAccessor;
    private readonly IMotionGraphicsPlacementAnnotator _placementAnnotator;
    private readonly ILogger<AgentStepExecutor> _logger;

    public AgentStepExecutor(
        IAgentRegistry agentRegistry,
        IWorkflowExecutionContextAccessor executionContextAccessor,
        IMotionGraphicsPlacementAnnotator placementAnnotator,
        ILogger<AgentStepExecutor> logger)
    {
        _agentRegistry = agentRegistry;
        _executionContextAccessor = executionContextAccessor;
        _placementAnnotator = placementAnnotator;
        _logger = logger;
    }

    public StepType StepType => StepType.Agent;

    public async Task<StepExecutionResult> ExecuteAsync(StepExecutionContext context)
    {
        Guid? customAgentId = context.Step.AgentDefinition.AgentType == AgentType.Custom
            ? context.Step.AgentDefinitionId
            : null;

        IReelForgeAgent? agent = _agentRegistry.GetByType(context.Step.AgentDefinition.AgentType, customAgentId);
        if (agent == null)
        {
            _logger.LogWarning(
                "No agent found for type {AgentType} (AgentDefinitionId: {AgentDefinitionId}), skipping",
                context.Step.AgentDefinition.AgentType,
                context.Step.AgentDefinitionId);
            return new StepExecutionResult
            {
                Output = context.AccumulatedOutput,
                NextStepIndex = context.CurrentStepIndex + 1,
                NewIterationCount = context.IterationCount,
                DurationMs = 0,
                TokensUsed = 0,
                Status = StepStatus.Skipped,
                ErrorDetails = $"No agent registered for type {context.Step.AgentDefinition.AgentType} and agent definition id {context.Step.AgentDefinitionId}"
            };
        }

        // Narrowly scoped, agent-type-specific INPUT augmentation (the mirror image of the
        // AgentType.AuthorAgent-specific OUTPUT validation further down): the motion-graphics
        // planner is the one agent whose prompt contains overlay-placement candidates AND, via
        // AgentInputContextMode.FullWorkflow, the story editor's already-made cut decision — so it
        // is the first and only point in the pipeline where "does this placement survive the cut?"
        // is answerable at all. Display-only: it adds an `inEdit` flag to each candidate, never
        // removes one. A no-op for every other agent type, and internally soft-failing (see
        // IMotionGraphicsPlacementAnnotator), so the prompt is simply un-annotated if anything
        // about the resolution goes wrong.
        if (context.Step.AgentDefinition.AgentType == AgentType.MotionGraphicsPlanner)
            await _placementAnnotator.AnnotateAsync(context, context.CancellationToken);

        string stepInput = context.BuildAgentInput();

        _logger.LogInformation(
            "Executing agent step {StepOrder}: {AgentName} (AgentType={AgentType}, AgentDefinitionId={AgentDefinitionId}, Tools={ToolCount})",
            context.Step.StepOrder,
            agent.Name,
            context.Step.AgentDefinition.AgentType,
            context.Step.AgentDefinitionId,
            agent.Tools.Count);
        _logger.LogDebug(
            "Agent step {StepOrder} input prepared (Chars={InputChars}, Preview={InputPreview})",
            context.Step.StepOrder,
            stepInput.Length,
            CreatePreview(stepInput, 300));

        Stopwatch sw = Stopwatch.StartNew();
        using IDisposable _ = _executionContextAccessor.BeginScope(
            context.Execution.Id,
            context.Execution.ProjectId,
            context.CorrelationId);
        AgentRunResult result = await agent.RunAsync(stepInput, context.Step.AgentDefinitionId, context.CancellationToken);
        sw.Stop();

        _logger.LogInformation(
            "Agent step {StepOrder}: {AgentName} completed in {DurationMs}ms (Tokens={TotalTokens}, InputTokens={InputTokens}, OutputTokens={OutputTokens}, Success={Success})",
            context.Step.StepOrder,
            agent.Name,
            sw.ElapsedMilliseconds,
            result.TokensUsed,
            result.InputTokens,
            result.OutputTokens,
            result.Success);
        _logger.LogDebug(
            "Agent step {StepOrder} output preview: {OutputPreview}",
            context.Step.StepOrder,
            CreatePreview(result.Output, 300));

        string? outputStorageKey = _executionContextAccessor.Current?.PendingOutputStorageKey;

        if (!result.Success)
        {
            string failureReason = BuildFailureReason(result.Output, result.FailureReason, agent.Name);
            _logger.LogWarning(
                "Agent step {StepOrder}: {AgentName} returned failed status. Reason={FailureReason}",
                context.Step.StepOrder,
                agent.Name,
                failureReason);
            return new StepExecutionResult
            {
                Output = result.Output,
                NextStepIndex = context.CurrentStepIndex + 1,
                NewIterationCount = context.IterationCount,
                DurationMs = sw.ElapsedMilliseconds,
                TokensUsed = result.TokensUsed,
                InputTokens = result.InputTokens,
                OutputTokens = result.OutputTokens,
                ToolCalls = result.ToolCalls,
                Reasoning = result.Reasoning,
                Status = StepStatus.Failed,
                ErrorDetails = failureReason,
                OutputStorageKey = outputStorageKey
            };
        }

        if (context.Step.AgentDefinition.AgentType == AgentType.AuthorAgent)
        {
            if (TryDetectAuthorOutputFailure(result.Output, out string failureReason))
            {
                return new StepExecutionResult
                {
                    Output = result.Output,
                    NextStepIndex = context.CurrentStepIndex + 1,
                    NewIterationCount = context.IterationCount,
                    DurationMs = sw.ElapsedMilliseconds,
                    TokensUsed = result.TokensUsed,
                    InputTokens = result.InputTokens,
                    OutputTokens = result.OutputTokens,
                    ToolCalls = result.ToolCalls,
                    Reasoning = result.Reasoning,
                    Status = StepStatus.Failed,
                    ErrorDetails = failureReason,
                    OutputStorageKey = outputStorageKey
                };
            }

            if (string.IsNullOrWhiteSpace(outputStorageKey))
            {
                return new StepExecutionResult
                {
                    Output = result.Output,
                    NextStepIndex = context.CurrentStepIndex + 1,
                    NewIterationCount = context.IterationCount,
                    DurationMs = sw.ElapsedMilliseconds,
                    TokensUsed = result.TokensUsed,
                    InputTokens = result.InputTokens,
                    OutputTokens = result.OutputTokens,
                    ToolCalls = result.ToolCalls,
                    Reasoning = result.Reasoning,
                    Status = StepStatus.Failed,
                    ErrorDetails = "Author step did not produce a rendered media artifact (missing outputStorageKey).",
                    OutputStorageKey = outputStorageKey
                };
            }
        }

        return new StepExecutionResult
        {
            Output = result.Output,
            NextStepIndex = context.CurrentStepIndex + 1,
            NewIterationCount = context.IterationCount,
            DurationMs = sw.ElapsedMilliseconds,
            TokensUsed = result.TokensUsed,
            InputTokens = result.InputTokens,
            OutputTokens = result.OutputTokens,
            ToolCalls = result.ToolCalls,
            Reasoning = result.Reasoning,
            Status = StepStatus.Completed,
            OutputStorageKey = outputStorageKey
        };
    }

    private static bool TryDetectAuthorOutputFailure(string? output, out string failureReason)
    {
        failureReason = string.Empty;

        if (string.IsNullOrWhiteSpace(output))
            return false;

        if (TryDetectJsonFailure(output, out failureReason))
            return true;

        if (ContainsTypecheckFailureSignature(output))
        {
            failureReason = "Author step output indicates tool/typecheck errors.";
            return true;
        }

        return false;
    }

    private static string CreatePreview(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string normalized = value.Replace("\r", " ").Replace("\n", " ").Trim();
        if (normalized.Length <= maxLength)
            return normalized;

        return normalized[..maxLength];
    }

    private static bool TryDetectJsonFailure(string output, out string failureReason)
    {
        failureReason = string.Empty;

        try
        {
            using JsonDocument document = JsonDocument.Parse(output);

            if (TryFindBooleanLikeProperty(document.RootElement, "hasErrors", true))
            {
                failureReason = "Author step output indicates tool/typecheck errors (hasErrors=true).";
                return true;
            }

            if (TryFindBooleanLikeProperty(document.RootElement, "success", false))
            {
                failureReason = "Author step output indicates failure (success=false).";
                return true;
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryFindBooleanLikeProperty(JsonElement element, string propertyName, bool expectedValue)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (property.NameEquals(propertyName) || string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryGetBooleanLikeValue(property.Value, out bool parsedValue) && parsedValue == expectedValue)
                            return true;
                    }

                    if (TryFindBooleanLikeProperty(property.Value, propertyName, expectedValue))
                        return true;
                }

                break;

            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    if (TryFindBooleanLikeProperty(item, propertyName, expectedValue))
                        return true;
                }

                break;
        }

        return false;
    }

    private static bool TryGetBooleanLikeValue(JsonElement value, out bool parsed)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.True:
                parsed = true;
                return true;
            case JsonValueKind.False:
                parsed = false;
                return true;
            case JsonValueKind.String:
                {
                    string stringValue = value.GetString() ?? string.Empty;
                    if (bool.TryParse(stringValue, out bool boolResult))
                    {
                        parsed = boolResult;
                        return true;
                    }

                    if (stringValue == "1")
                    {
                        parsed = true;
                        return true;
                    }

                    if (stringValue == "0")
                    {
                        parsed = false;
                        return true;
                    }

                    break;
                }
            case JsonValueKind.Number:
                if (value.TryGetInt32(out int intValue))
                {
                    if (intValue == 1)
                    {
                        parsed = true;
                        return true;
                    }

                    if (intValue == 0)
                    {
                        parsed = false;
                        return true;
                    }
                }

                break;
        }

        parsed = default;
        return false;
    }

    private static bool ContainsTypecheckFailureSignature(string output)
    {
        return output.Contains("error TS", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Cannot find module", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildFailureReason(string? output, string? explicitReason, string agentName)
    {
        if (!string.IsNullOrWhiteSpace(explicitReason))
            return explicitReason.Trim();

        if (string.IsNullOrWhiteSpace(output))
            return $"Agent {agentName} reported failure.";

        string[] lines = output
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line =>
                line.Contains("error", StringComparison.OrdinalIgnoreCase)
                || line.Contains("TS", StringComparison.Ordinal)
                || line.Contains("Cannot find module", StringComparison.OrdinalIgnoreCase)
                || line.Contains("failed", StringComparison.OrdinalIgnoreCase))
            .Take(6)
            .ToArray();

        if (lines.Length == 0)
            return $"Agent {agentName} reported failure. Output preview: {CreatePreview(output, 220)}";

        StringBuilder builder = new($"Agent {agentName} reported failure diagnostics:");
        foreach (string line in lines)
            builder.Append($"\n- {line}");

        return builder.ToString();
    }

}
