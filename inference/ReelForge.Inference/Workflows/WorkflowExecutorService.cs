using System.Diagnostics;
using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.EntityFrameworkCore;
using ReelForge.Inference.Agents;
using ReelForge.Inference.Data;
using ReelForge.Inference.Data.Models;

namespace ReelForge.Inference.Workflows;

/// <summary>
/// Service that dynamically constructs and executes workflows from database definitions.
/// </summary>
public class WorkflowExecutorService
{
    private readonly IAgentRegistry _agentRegistry;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WorkflowExecutorService> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public WorkflowExecutorService(
        IAgentRegistry agentRegistry,
        IServiceScopeFactory scopeFactory,
        ILogger<WorkflowExecutorService> logger,
        ILoggerFactory loggerFactory)
    {
        _agentRegistry = agentRegistry;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Executes a workflow for the given execution record.
    /// </summary>
    public async Task ExecuteAsync(Guid executionId, CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        ReelForgeDbContext db = scope.ServiceProvider.GetRequiredService<ReelForgeDbContext>();

        WorkflowExecution? execution = await db.WorkflowExecutions
            .Include(e => e.WorkflowDefinition)
                .ThenInclude(w => w.Steps)
                    .ThenInclude(s => s.AgentDefinition)
            .FirstOrDefaultAsync(e => e.Id == executionId, ct);

        if (execution == null)
        {
            _logger.LogError("Workflow execution {ExecutionId} not found", executionId);
            return;
        }

        execution.Status = ExecutionStatus.Running;
        execution.StartedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        List<WorkflowStep> steps = execution.WorkflowDefinition.Steps
            .OrderBy(s => s.StepOrder)
            .ToList();

        try
        {
            string accumulatedOutput = string.Empty;
            int maxIterations = 3;
            int iterationCount = 0;
            int directorStepIndex = steps.FindIndex(s => s.AgentDefinition.AgentType == AgentType.DirectorAgent);

            // Start from the first step
            int currentStepIndex = 0;

            while (currentStepIndex < steps.Count && !ct.IsCancellationRequested)
            {
                WorkflowStep step = steps[currentStepIndex];
                IReelForgeAgent? agent = _agentRegistry.GetByType(step.AgentDefinition.AgentType);

                if (agent == null)
                {
                    _logger.LogWarning("No agent found for type {AgentType}, skipping step", step.AgentDefinition.AgentType);
                    currentStepIndex++;
                    continue;
                }

                execution.CurrentStepId = step.Id;
                await db.SaveChangesAsync(ct);

                _logger.LogInformation("Executing step {StepOrder}: {AgentName}", step.StepOrder, agent.Name);

                Stopwatch sw = Stopwatch.StartNew();
                string stepInput = string.IsNullOrEmpty(accumulatedOutput)
                    ? "Begin analysis of the project."
                    : accumulatedOutput;

                string output = await agent.RunAsync(stepInput, ct);
                sw.Stop();

                // Persist step result
                WorkflowStepResult stepResult = new()
                {
                    Id = Guid.NewGuid(),
                    WorkflowExecutionId = executionId,
                    WorkflowStepId = step.Id,
                    Output = output,
                    TokensUsed = 0, // Token tracking would require callback integration
                    DurationMs = sw.ElapsedMilliseconds,
                    ExecutedAt = DateTime.UtcNow
                };
                db.WorkflowStepResults.Add(stepResult);
                await db.SaveChangesAsync(ct);

                accumulatedOutput = output;

                // Handle review loop
                if (step.AgentDefinition.AgentType == AgentType.ReviewAgent)
                {
                    iterationCount++;
                    execution.IterationCount = iterationCount;

                    int score = ParseReviewScore(output);
                    ReviewScore reviewScore = new()
                    {
                        Id = Guid.NewGuid(),
                        WorkflowExecutionId = executionId,
                        IterationNumber = iterationCount,
                        Score = score,
                        Comments = output,
                        CreatedAt = DateTime.UtcNow
                    };
                    db.ReviewScores.Add(reviewScore);
                    await db.SaveChangesAsync(ct);

                    if (score < 9 && iterationCount < maxIterations && directorStepIndex >= 0)
                    {
                        _logger.LogInformation("Review score {Score} < 9, iteration {Iteration}/{Max}, looping back to Director",
                            score, iterationCount, maxIterations);
                        currentStepIndex = directorStepIndex;
                        continue;
                    }
                }

                currentStepIndex++;
            }

            execution.Status = ExecutionStatus.Passed;
            execution.ResultJson = accumulatedOutput;
            execution.CompletedAt = DateTime.UtcNow;
            execution.CurrentStepId = null;
            await db.SaveChangesAsync(ct);

            _logger.LogInformation("Workflow execution {ExecutionId} completed successfully", executionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow execution {ExecutionId} failed", executionId);
            execution.Status = ExecutionStatus.Failed;
            execution.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
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
        catch (JsonException)
        {
            // Fall through
        }
        return 0;
    }
}
