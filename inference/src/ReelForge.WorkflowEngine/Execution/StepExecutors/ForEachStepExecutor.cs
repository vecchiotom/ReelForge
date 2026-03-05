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
    private readonly ILogger<ForEachStepExecutor> _logger;

    public ForEachStepExecutor(IAgentRegistry agentRegistry, ILogger<ForEachStepExecutor> logger)
    {
        _agentRegistry = agentRegistry;
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
        var results = new List<string>();
        long totalDuration = 0;

        for (int i = 0; i < Math.Min(items.Count, maxIterations); i++)
        {
            Stopwatch sw = Stopwatch.StartNew();
            string output = await agent.RunAsync(items[i], context.CancellationToken);
            sw.Stop();
            totalDuration += sw.ElapsedMilliseconds;
            results.Add(output);
        }

        string aggregatedOutput = JsonSerializer.Serialize(new { results, sourceCount = items.Count });

        return new StepExecutionResult
        {
            Output = aggregatedOutput,
            NextStepIndex = context.CurrentStepIndex + 1,
            NewIterationCount = context.IterationCount,
            DurationMs = totalDuration,
            Status = StepStatus.Completed
        };
    }
}
