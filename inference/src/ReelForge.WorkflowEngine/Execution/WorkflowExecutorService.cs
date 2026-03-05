using System.Diagnostics;
using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.IntegrationEvents;
using ReelForge.WorkflowEngine.Data;
using ReelForge.WorkflowEngine.Observability;

namespace ReelForge.WorkflowEngine.Execution;

/// <summary>
/// Enhanced workflow executor using the step executor strategy pattern.
/// </summary>
public class WorkflowExecutorService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWorkflowEventPublisher _eventPublisher;
    private readonly ILogger<WorkflowExecutorService> _logger;
    private readonly Dictionary<StepType, IStepExecutor> _executors;
    private readonly IPublishEndpoint _publishEndpoint;

    public WorkflowExecutorService(
        IServiceScopeFactory scopeFactory,
        IWorkflowEventPublisher eventPublisher,
        ILogger<WorkflowExecutorService> logger,
        IEnumerable<IStepExecutor> executors,
        IPublishEndpoint publishEndpoint)
    {
        _scopeFactory = scopeFactory;
        _eventPublisher = eventPublisher;
        _logger = logger;
        _executors = executors.ToDictionary(e => e.StepType);
        _publishEndpoint = publishEndpoint;
    }

    public async Task ExecuteAsync(Guid executionId, string correlationId, CancellationToken ct)
    {
        using Activity? workflowActivity = ReelForgeDiagnostics.ActivitySource.StartActivity("ExecuteWorkflow");
        workflowActivity?.SetTag("execution.id", executionId.ToString());
        workflowActivity?.SetTag("correlation.id", correlationId);

        ReelForgeDiagnostics.ActiveWorkflows.Add(1);

        using IServiceScope scope = _scopeFactory.CreateScope();
        WorkflowEngineDbContext db = scope.ServiceProvider.GetRequiredService<WorkflowEngineDbContext>();

        WorkflowExecution? execution = await db.WorkflowExecutions
            .Include(e => e.WorkflowDefinition)
                .ThenInclude(w => w.Steps)
                    .ThenInclude(s => s.AgentDefinition)
            .FirstOrDefaultAsync(e => e.Id == executionId, ct);

        if (execution == null)
        {
            _logger.LogError("Workflow execution {ExecutionId} not found", executionId);
            ReelForgeDiagnostics.ActiveWorkflows.Add(-1);
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
            int iterationCount = 0;
            int currentStepIndex = 0;

            while (currentStepIndex < steps.Count && !ct.IsCancellationRequested)
            {
                WorkflowStep step = steps[currentStepIndex];

                using Activity? stepActivity = ReelForgeDiagnostics.ActivitySource.StartActivity("ExecuteStep");
                stepActivity?.SetTag("step.order", step.StepOrder);
                stepActivity?.SetTag("step.type", step.StepType.ToString());
                stepActivity?.SetTag("agent.type", step.AgentDefinition.AgentType.ToString());

                execution.CurrentStepId = step.Id;
                await db.SaveChangesAsync(ct);

                if (!_executors.TryGetValue(step.StepType, out IStepExecutor? executor))
                {
                    _logger.LogWarning("No executor for step type {StepType}, using Agent executor", step.StepType);
                    executor = _executors[StepType.Agent];
                }

                StepExecutionContext context = new()
                {
                    Execution = execution,
                    Step = step,
                    AllSteps = steps,
                    AccumulatedOutput = accumulatedOutput,
                    CurrentStepIndex = currentStepIndex,
                    IterationCount = iterationCount,
                    CorrelationId = correlationId,
                    CancellationToken = ct
                };

                StepExecutionResult result = await executor.ExecuteAsync(context);

                // Persist step result
                WorkflowStepResult stepResult = new()
                {
                    Id = Guid.NewGuid(),
                    WorkflowExecutionId = executionId,
                    WorkflowStepId = step.Id,
                    Output = result.Output,
                    TokensUsed = result.TokensUsed,
                    DurationMs = result.DurationMs,
                    ExecutedAt = DateTime.UtcNow,
                    InputJson = accumulatedOutput.Length > 0 ? accumulatedOutput : null,
                    OutputJson = result.Output,
                    Status = result.Status,
                    ErrorDetails = result.ErrorDetails,
                    IterationNumber = result.IterationNumber,
                    CompletedAt = DateTime.UtcNow
                };
                db.WorkflowStepResults.Add(stepResult);

                // Handle review scores for ReviewLoop steps
                if (step.StepType == StepType.ReviewLoop && result.IterationNumber.HasValue)
                {
                    int score = ParseReviewScore(result.Output);
                    db.ReviewScores.Add(new ReviewScore
                    {
                        Id = Guid.NewGuid(),
                        WorkflowExecutionId = executionId,
                        IterationNumber = result.IterationNumber.Value,
                        Score = score,
                        Comments = result.Output,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                await db.SaveChangesAsync(ct);
                await _eventPublisher.PublishStepCompletedAsync(execution, step, stepResult, ct);

                // Publish step-level event for real-time monitoring.
                await _publishEndpoint.Publish(new WorkflowStepCompleted
                {
                    ExecutionId = executionId,
                    StepId = step.Id,
                    StepResultId = stepResult.Id,
                    CorrelationId = correlationId,
                    StepStatus = result.Status.ToString(),
                    TokensUsed = result.TokensUsed,
                    DurationMs = result.DurationMs,
                    CompletedAt = DateTime.UtcNow
                }, ct);

                ReelForgeDiagnostics.StepDuration.Record(result.DurationMs,
                    new KeyValuePair<string, object?>("step.type", step.StepType.ToString()),
                    new KeyValuePair<string, object?>("agent.type", step.AgentDefinition.AgentType.ToString()));

                accumulatedOutput = result.Output;
                iterationCount = result.NewIterationCount;
                currentStepIndex = result.NextStepIndex;

                execution.IterationCount = iterationCount;
            }

            execution.Status = ExecutionStatus.Passed;
            execution.ResultJson = accumulatedOutput;
            execution.CompletedAt = DateTime.UtcNow;
            execution.CurrentStepId = null;
            await db.SaveChangesAsync(ct);
            await _eventPublisher.PublishExecutionCompletedAsync(execution, ct);

            ReelForgeDiagnostics.CompletedWorkflows.Add(1,
                new KeyValuePair<string, object?>("status", "passed"));

            _logger.LogInformation("Workflow execution {ExecutionId} completed successfully", executionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow execution {ExecutionId} failed", executionId);
            execution.Status = ExecutionStatus.Failed;
            execution.ErrorMessage = ex.Message;
            execution.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            await _eventPublisher.PublishExecutionFailedAsync(execution, ct);

            ReelForgeDiagnostics.CompletedWorkflows.Add(1,
                new KeyValuePair<string, object?>("status", "failed"));

            throw;
        }
        finally
        {
            ReelForgeDiagnostics.ActiveWorkflows.Add(-1);
        }
    }

    private static int ParseReviewScore(string reviewOutput)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(reviewOutput);
            if (doc.RootElement.TryGetProperty("score", out JsonElement scoreProp))
                return scoreProp.GetInt32();
        }
        catch (JsonException) { }
        return 0;
    }
}
