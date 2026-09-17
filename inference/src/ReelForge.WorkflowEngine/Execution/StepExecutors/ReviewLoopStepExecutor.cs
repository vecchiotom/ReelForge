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

    /// <summary>
    /// Internal (not private) so both the main pipeline and video-editing review outputs' scores
    /// are directly unit-testable. Checks "score" first — the property name
    /// <c>AgentType.VideoReviewAgent</c>'s <c>VideoReviewOutput.Score</c> serializes as — then
    /// falls back to "overallScore", the property name the main pipeline's
    /// <c>AgentType.ReviewAgent</c> (<c>ReviewOutput.OverallScore</c>) actually serializes as. The
    /// fallback fixes a real pre-existing gap: this method previously only ever checked "score",
    /// which does not exist at the root of a ReviewOutput completion, so the main pipeline's
    /// review score silently always parsed as 0 (always below MinScore) regardless of what the
    /// review agent actually judged — found while wiring this same method for the new
    /// video-editing review loop, which needed to confirm the property name it should use.
    /// </summary>
    internal static int ParseReviewScore(string reviewOutput)
    {
        try
        {
            string json = RobustJsonExtractor.ExtractJsonObject(reviewOutput) ?? reviewOutput;
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("score", out JsonElement scoreProp) &&
                scoreProp.ValueKind == JsonValueKind.Number)
            {
                return ReadScore(scoreProp);
            }
            if (doc.RootElement.TryGetProperty("overallScore", out JsonElement overallScoreProp) &&
                overallScoreProp.ValueKind == JsonValueKind.Number)
            {
                return ReadScore(overallScoreProp);
            }
        }
        catch (JsonException) { }
        return 0;
    }

    /// <summary>
    /// Reads a review score as an int, tolerating a non-integer JSON number (e.g. <c>8.5</c> —
    /// entirely plausible output for a "score 1-10" prompt). <see cref="JsonElement.GetInt32"/>
    /// throws <see cref="FormatException"/> — NOT <see cref="JsonException"/> — for a non-integer
    /// number, which the caller's <c>catch (JsonException)</c> does not cover, so an uncaught
    /// exception would previously propagate out of this "never fails the loop" scoring helper.
    /// Falls back to the rounded double value, then clamps to a sane 0-10 range.
    /// </summary>
    private static int ReadScore(JsonElement scoreProp)
    {
        int score = scoreProp.TryGetInt32(out int intValue)
            ? intValue
            : (int)Math.Round(scoreProp.GetDouble(), MidpointRounding.AwayFromZero);

        return Math.Clamp(score, 0, 10);
    }

    /// <summary>
    /// Builds a short, prose feedback string from a review agent's raw JSON output — schema-
    /// tolerant across both <c>ReviewOutput</c> ("summary" + "improvementAreas") and
    /// <c>VideoReviewOutput</c> ("summary" + "issues") shapes, since both flow through this same
    /// ReviewLoop machinery. Used by <c>WorkflowExecutorService</c> to seed the loop-back target
    /// step's <see cref="Execution.StepExecutionContext.RetryFeedback"/>/<c>RetryGuidance</c> —
    /// the same mechanism <c>AgentStepExecutor</c> already uses for schema-validation retries —
    /// so the next attempt gets the actual critique instead of retrying blind. Returns null when
    /// nothing usable could be extracted (malformed JSON, or no summary/issues present at all).
    /// </summary>
    internal static string? ExtractFeedbackSummary(string reviewOutput)
    {
        try
        {
            string json = RobustJsonExtractor.ExtractJsonObject(reviewOutput) ?? reviewOutput;
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            List<string> parts = new();

            if (root.TryGetProperty("summary", out JsonElement summaryProp) &&
                summaryProp.ValueKind == JsonValueKind.String)
            {
                string? summary = summaryProp.GetString();
                if (!string.IsNullOrWhiteSpace(summary))
                    parts.Add(summary.Trim());
            }

            List<string> issues = new();
            foreach (string propName in new[] { "issues", "improvementAreas" })
            {
                if (root.TryGetProperty(propName, out JsonElement arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in arr.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.String)
                            continue;

                        string? s = item.GetString();
                        if (!string.IsNullOrWhiteSpace(s))
                            issues.Add(s.Trim());
                    }
                }
            }

            if (issues.Count > 0)
                parts.Add("Issues: " + string.Join("; ", issues));

            return parts.Count == 0 ? null : string.Join(" ", parts);
        }
        catch (JsonException)
        {
            return null;
        }
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
