using System.Diagnostics;
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
    private readonly ILogger<AgentStepExecutor> _logger;

    public AgentStepExecutor(
        IAgentRegistry agentRegistry,
        IWorkflowExecutionContextAccessor executionContextAccessor,
        ILogger<AgentStepExecutor> logger)
    {
        _agentRegistry = agentRegistry;
        _executionContextAccessor = executionContextAccessor;
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

        string stepInput = context.BuildAgentInput();

        _logger.LogInformation("Executing agent step {StepOrder}: {AgentName}",
            context.Step.StepOrder, agent.Name);

        Stopwatch sw = Stopwatch.StartNew();
        using IDisposable _ = _executionContextAccessor.BeginScope(
            context.Execution.Id,
            context.Execution.ProjectId,
            context.CorrelationId);
        AgentRunResult result = await agent.RunAsync(stepInput, context.CancellationToken);
        sw.Stop();

        string? outputStorageKey = _executionContextAccessor.Current?.PendingOutputStorageKey;

        if (!result.Success)
        {
            return new StepExecutionResult
            {
                Output = result.Output,
                NextStepIndex = context.CurrentStepIndex + 1,
                NewIterationCount = context.IterationCount,
                DurationMs = sw.ElapsedMilliseconds,
                TokensUsed = result.TokensUsed,
                Status = StepStatus.Failed,
                ErrorDetails = string.IsNullOrWhiteSpace(result.FailureReason)
                    ? $"Agent {agent.Name} reported failure."
                    : result.FailureReason,
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
}
