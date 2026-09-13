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
    private readonly IWorkflowExecutionContextAccessor _executionContextAccessor;
    private readonly ILogger<ReviewLoopStepExecutor> _logger;

    public ReviewLoopStepExecutor(
        IAgentRegistry agentRegistry,
        IWorkflowExecutionContextAccessor executionContextAccessor,
        ILogger<ReviewLoopStepExecutor> logger)
    {
        _agentRegistry = agentRegistry;
        _executionContextAccessor = executionContextAccessor;
        _logger = logger;
    }

    public StepType StepType => StepType.ReviewLoop;

    public async Task<StepExecutionResult> ExecuteAsync(StepExecutionContext context)
    {
        IReelForgeAgent? agent = _agentRegistry.GetByType(context.Step.AgentDefinition.AgentType);
        if (agent == null)
        {
            _logger.LogWarning(
                "ReviewLoop step {StepOrder}: no agent registered for type {AgentType}",
                context.Step.StepOrder,
                context.Step.AgentDefinition.AgentType);
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
        string input = context.BuildAgentInput();
        _logger.LogInformation(
            "ReviewLoop step {StepOrder}: executing agent {AgentName} (Iteration={CurrentIteration}, MaxIterations={MaxIterations}, MinScore={MinScore})",
            context.Step.StepOrder,
            agent.Name,
            context.IterationCount + 1,
            context.Step.MaxIterations,
            context.Step.MinScore ?? 9);
        _logger.LogDebug(
            "ReviewLoop step {StepOrder} input preview: {InputPreview}",
            context.Step.StepOrder,
            CreatePreview(input, 250));
        using IDisposable _ = _executionContextAccessor.BeginScope(
            context.Execution.Id,
            context.Execution.ProjectId,
            context.CorrelationId);
        AgentRunResult result = await agent.RunAsync(input, context.Step.AgentDefinitionId, context.CancellationToken);
        sw.Stop();

        int score = ParseReviewScore(result.Output);
        int minScore = context.Step.MinScore ?? 9;
        int maxIterations = context.Step.MaxIterations;
        int newIterationCount = context.IterationCount + 1;

        _logger.LogInformation(
            "ReviewLoop step {StepOrder}: score={Score}, minScore={MinScore}, iteration={Iteration}/{Max}, duration={DurationMs}ms, tokens={TokensUsed}",
            context.Step.StepOrder, score, minScore, newIterationCount, maxIterations, sw.ElapsedMilliseconds, result.TokensUsed);
        _logger.LogDebug(
            "ReviewLoop step {StepOrder} output preview: {OutputPreview}",
            context.Step.StepOrder,
            CreatePreview(result.Output, 250));

        // If score is below threshold and we haven't exceeded max iterations, loop back
        if (score < minScore && newIterationCount < maxIterations && context.Step.LoopTargetStepOrder.HasValue)
        {
            int targetIndex = context.AllSteps.FindIndex(s => s.StepOrder == context.Step.LoopTargetStepOrder.Value);
            if (targetIndex >= 0)
            {
                _logger.LogInformation("Looping back to step order {TargetOrder}", context.Step.LoopTargetStepOrder.Value);
                return new StepExecutionResult
                {
                    Output = result.Output,
                    NextStepIndex = targetIndex,
                    NewIterationCount = newIterationCount,
                    DurationMs = sw.ElapsedMilliseconds,
                    TokensUsed = result.TokensUsed,
                    Status = StepStatus.Completed,
                    IterationNumber = newIterationCount
                };
            }
        }

        return new StepExecutionResult
        {
            Output = result.Output,
            NextStepIndex = context.CurrentStepIndex + 1,
            NewIterationCount = newIterationCount,
            DurationMs = sw.ElapsedMilliseconds,
            TokensUsed = result.TokensUsed,
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

    private static string CreatePreview(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string normalized = value.Replace("\r", " ").Replace("\n", " ").Trim();
        if (normalized.Length <= maxLength)
            return normalized;

        return normalized[..maxLength];
    }
}
