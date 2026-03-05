using System.Diagnostics;
using System.Text.Json;
using ReelForge.Shared.Data.Models;
using ReelForge.WorkflowEngine.Agents;

namespace ReelForge.WorkflowEngine.Execution.StepExecutors;

/// <summary>
/// Runs the review agent, parses score, and loops back if score is below threshold.
/// </summary>
public class ReviewLoopStepExecutor : IStepExecutor
{
    private readonly IAgentRegistry _agentRegistry;
    private readonly ILogger<ReviewLoopStepExecutor> _logger;

    public ReviewLoopStepExecutor(IAgentRegistry agentRegistry, ILogger<ReviewLoopStepExecutor> logger)
    {
        _agentRegistry = agentRegistry;
        _logger = logger;
    }

    public StepType StepType => StepType.ReviewLoop;

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

        Stopwatch sw = Stopwatch.StartNew();
        string output = await agent.RunAsync(context.AccumulatedOutput, context.CancellationToken);
        sw.Stop();

        int score = ParseReviewScore(output);
        int minScore = context.Step.MinScore ?? 9;
        int maxIterations = context.Step.MaxIterations;
        int newIterationCount = context.IterationCount + 1;

        _logger.LogInformation(
            "ReviewLoop step {StepOrder}: score={Score}, minScore={MinScore}, iteration={Iteration}/{Max}",
            context.Step.StepOrder, score, minScore, newIterationCount, maxIterations);

        // If score is below threshold and we haven't exceeded max iterations, loop back
        if (score < minScore && newIterationCount < maxIterations && context.Step.LoopTargetStepOrder.HasValue)
        {
            int targetIndex = context.AllSteps.FindIndex(s => s.StepOrder == context.Step.LoopTargetStepOrder.Value);
            if (targetIndex >= 0)
            {
                _logger.LogInformation("Looping back to step order {TargetOrder}", context.Step.LoopTargetStepOrder.Value);
                return new StepExecutionResult
                {
                    Output = output,
                    NextStepIndex = targetIndex,
                    NewIterationCount = newIterationCount,
                    DurationMs = sw.ElapsedMilliseconds,
                    Status = StepStatus.Completed,
                    IterationNumber = newIterationCount
                };
            }
        }

        return new StepExecutionResult
        {
            Output = output,
            NextStepIndex = context.CurrentStepIndex + 1,
            NewIterationCount = newIterationCount,
            DurationMs = sw.ElapsedMilliseconds,
            Status = StepStatus.Completed,
            IterationNumber = newIterationCount
        };
    }

    private static int ParseReviewScore(string reviewOutput)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(reviewOutput);
            if (doc.RootElement.TryGetProperty("score", out JsonElement scoreProp))
            {
                return scoreProp.GetInt32();
            }
        }
        catch (JsonException) { }
        return 0;
    }
}
