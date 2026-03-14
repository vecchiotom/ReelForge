using System.Diagnostics;
using System.Text.Json;
using ReelForge.Shared.Data.Models;
using ReelForge.WorkflowEngine.Agents;

namespace ReelForge.WorkflowEngine.Execution.StepExecutors;

/// <summary>
/// Iterates over an array from previous output and runs the agent for each element.
/// </summary>
public class ForEachStepExecutor : IStepExecutor
{
    private readonly IAgentRegistry _agentRegistry;
    private readonly IWorkflowExecutionContextAccessor _executionContextAccessor;
    private readonly int _maxParallelism;
    private readonly ILogger<ForEachStepExecutor> _logger;

    public ForEachStepExecutor(
        IAgentRegistry agentRegistry,
        IWorkflowExecutionContextAccessor executionContextAccessor,
        IConfiguration configuration,
        ILogger<ForEachStepExecutor> logger)
    {
        _agentRegistry = agentRegistry;
        _executionContextAccessor = executionContextAccessor;
        _maxParallelism = Math.Max(1, configuration.GetValue("WorkflowEngine:AgentParallelism", 4));
        _logger = logger;
    }

    public StepType StepType => StepType.ForEach;

    public async Task<StepExecutionResult> ExecuteAsync(StepExecutionContext context)
    {
        IReelForgeAgent? agent = _agentRegistry.GetByType(context.Step.AgentDefinition.AgentType);
        if (agent == null)
        {
            return new StepExecutionResult
            {
                Output = context.AccumulatedOutput,
                NextStepIndex = context.CurrentStepIndex + 1,
                NewIterationCount = context.IterationCount,
                Status = StepStatus.Skipped,
                ErrorDetails = $"No agent for type {context.Step.AgentDefinition.AgentType}"
            };
        }

        List<string> items = ExpressionEvaluator.ExtractJsonArray(
            context.AccumulatedOutput,
            context.Step.LoopSourceExpression ?? "");

        if (items.Count == 0)
        {
            _logger.LogWarning("ForEach step {StepOrder}: no items found at path '{Path}'",
                context.Step.StepOrder, context.Step.LoopSourceExpression);
            return new StepExecutionResult
            {
                Output = context.AccumulatedOutput,
                NextStepIndex = context.CurrentStepIndex + 1,
                NewIterationCount = context.IterationCount,
                Status = StepStatus.Completed
            };
        }

        _logger.LogInformation("ForEach step {StepOrder}: processing {Count} items",
            context.Step.StepOrder, items.Count);

        int maxIterations = context.Step.MaxIterations > 0 ? context.Step.MaxIterations : items.Count;
        int iterations = Math.Min(items.Count, maxIterations);
        string[] results = new string[iterations];
        long totalDuration = 0;
        int totalTokens = 0;
        using IDisposable _ = _executionContextAccessor.BeginScope(
            context.Execution.Id,
            context.Execution.ProjectId,
            context.CorrelationId);

        await Parallel.ForEachAsync(
            Enumerable.Range(0, iterations),
            new ParallelOptions
            {
                CancellationToken = context.CancellationToken,
                MaxDegreeOfParallelism = _maxParallelism
            },
            async (index, token) =>
            {
                // For each item: prepend the user request context if present
                string itemInput = string.IsNullOrWhiteSpace(context.UserRequest)
                    ? items[index]
                    : $"{items[index]}\n\n---\nUser Request:\n{context.UserRequest}";

                if (!string.IsNullOrWhiteSpace(context.RetryGuidance))
                    itemInput = $"{itemInput}\n\n---\nRetry Guidance:\n{context.RetryGuidance}";

                Stopwatch sw = Stopwatch.StartNew();
                AgentRunResult result = await agent.RunAsync(itemInput, token);
                sw.Stop();
                Interlocked.Add(ref totalDuration, sw.ElapsedMilliseconds);
                Interlocked.Add(ref totalTokens, result.TokensUsed);
                results[index] = result.Output;
            });

        string aggregatedOutput = JsonSerializer.Serialize(new { results, sourceCount = items.Count });

        return new StepExecutionResult
        {
            Output = aggregatedOutput,
            NextStepIndex = context.CurrentStepIndex + 1,
            NewIterationCount = context.IterationCount,
            DurationMs = totalDuration,
            TokensUsed = totalTokens,
            Status = StepStatus.Completed
        };
    }
}
