using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.IntegrationEvents;
using ReelForge.WorkflowEngine.Data;
using ReelForge.WorkflowEngine.Observability;
using ReelForge.WorkflowEngine.Agents.Tools;
using ReelForge.WorkflowEngine.Execution.StepExecutors;
using ReelForge.WorkflowEngine.Services.Messaging;

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
    private readonly RabbitMqHelper _rabbitHelper;
    private readonly WorkflowHardeningOptions _hardeningOptions;

    // Cancellation tokens for running executions live in a separate SINGLETON
    // (ExecutionCancellationRegistry — see its doc comment for why), not as an instance field
    // here: this service is registered Scoped (it depends on the Scoped IWorkflowEventPublisher),
    // so a stop request arriving in its own DI scope/consumer instance would otherwise get a
    // brand-new WorkflowExecutorService with its own empty dictionary, sharing nothing with the
    // instance actually running the execution it's trying to cancel.
    private readonly ExecutionCancellationRegistry _cancellationRegistry;

    public WorkflowExecutorService(
        IServiceScopeFactory scopeFactory,
        IWorkflowEventPublisher eventPublisher,
        ILogger<WorkflowExecutorService> logger,
        IEnumerable<IStepExecutor> executors,
        RabbitMqHelper rabbitHelper,
        IOptions<WorkflowHardeningOptions> hardeningOptions,
        ExecutionCancellationRegistry cancellationRegistry)
    {
        _scopeFactory = scopeFactory;
        _eventPublisher = eventPublisher;
        _logger = logger;
        _executors = executors.ToDictionary(e => e.StepType);
        _rabbitHelper = rabbitHelper;
        _hardeningOptions = hardeningOptions.Value;
        _cancellationRegistry = cancellationRegistry;
    }

    public async Task ExecuteAsync(Guid executionId, string correlationId, CancellationToken ct)
    {
        using Activity? workflowActivity = ReelForgeDiagnostics.ActivitySource.StartActivity("ExecuteWorkflow");
        workflowActivity?.SetTag("execution.id", executionId.ToString());
        workflowActivity?.SetTag("correlation.id", correlationId);

        ReelForgeDiagnostics.ActiveWorkflows.Add(1);

        // create a linked cancellation token source so we can cancel from outside via CancelExecutionAsync
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        ct = linkedCts.Token;
        _cancellationRegistry.Register(executionId, linkedCts);

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
            _cancellationRegistry.Remove(executionId);
            return;
        }

        // only queued executions are eligible to start; this prevents redelivery
        // or duplicate messages from re-running a completed/cancelled execution
        if (execution.Status != ExecutionStatus.Queued)
        {
            _logger.LogInformation(
                "Execution {ExecutionId} is in status {Status} and will not be started again",
                executionId,
                execution.Status);
            ReelForgeDiagnostics.ActiveWorkflows.Add(-1);
            _cancellationRegistry.Remove(executionId);
            return;
        }

        execution.Status = ExecutionStatus.Running;
        execution.StartedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await _eventPublisher.PublishExecutionRunningAsync(execution, ct);

        List<WorkflowStep> steps = execution.WorkflowDefinition.Steps
            .OrderBy(s => s.StepOrder)
            .ToList();

        _logger.LogInformation(
            "Starting workflow execution {ExecutionId} for project {ProjectId} with {StepCount} steps (CorrelationId={CorrelationId})",
            execution.Id,
            execution.ProjectId,
            steps.Count,
            correlationId);

        try
        {
            string accumulatedOutput = string.Empty;
            int iterationCount = 0;
            int currentStepIndex = 0;
            var stepOutputHistory = new List<StepOutputHistoryEntry>();

            int stepTransitionCount = 0;
            const int MaxStepTransitions = 1000;

            // Feedback from a ReviewLoop step that just looped execution backward — seeded onto
            // every step's StepExecutionContext from the loop target through (but not including)
            // the ReviewLoop step itself, via the same RetryFeedback/RetryGuidance mechanism
            // AgentStepExecutor already uses for schema-validation retries (see
            // StepExecutionContext.RecordRetryFeedback), so a retried VideoStoryEditor/
            // MotionGraphicsPlanner/AuthorAgent gets the actual review critique instead of
            // retrying blind. Cleared once the window is exited (either the ReviewLoop step is
            // reached again, or it passed/exhausted MaxIterations and moved forward) so a later,
            // unrelated ReviewLoop step in the same workflow never inherits stale feedback.
            string? pendingReviewFeedback = null;
            int pendingReviewFeedbackMinStepOrder = int.MaxValue;
            int pendingReviewFeedbackMaxStepOrderExclusive = int.MinValue;

            while (currentStepIndex < steps.Count && !ct.IsCancellationRequested)
            {
                stepTransitionCount++;
                if (stepTransitionCount > MaxStepTransitions)
                {
                    throw new InvalidOperationException(
                        "Workflow execution exceeded the maximum allowed step transitions (possible infinite loop).");
                }

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

                _logger.LogInformation(
                    "Dispatching execution {ExecutionId} step {StepOrder} ({StepType}) to executor {ExecutorType} (Iteration={IterationCount}, CurrentStepIndex={CurrentStepIndex})",
                    executionId,
                    step.StepOrder,
                    step.StepType,
                    executor.GetType().Name,
                    iterationCount,
                    currentStepIndex);

                StepExecutionContext context = new()
                {
                    Execution = execution,
                    Step = step,
                    AllSteps = steps,
                    AccumulatedOutput = accumulatedOutput,
                    StepOutputHistory = stepOutputHistory,
                    CurrentStepIndex = currentStepIndex,
                    IterationCount = iterationCount,
                    CorrelationId = correlationId,
                    UserRequest = execution.UserRequest,
                    CancellationToken = ct
                };

                if (pendingReviewFeedback is not null &&
                    IsWithinReviewFeedbackWindow(step.StepOrder, pendingReviewFeedbackMinStepOrder, pendingReviewFeedbackMaxStepOrderExclusive))
                {
                    context.RecordRetryFeedback(iterationCount, pendingReviewFeedback);
                }

                string? initialInputJson = ResolveInputJsonForPersistence(context.LastResolvedAgentInput, accumulatedOutput);
                if (step.StepType == StepType.Agent)
                {
                    string _ = context.BuildAgentInput();
                    initialInputJson = ResolveInputJsonForPersistence(context.LastResolvedAgentInput, accumulatedOutput);
                }

                WorkflowStepResult stepResult = new()
                {
                    Id = Guid.NewGuid(),
                    WorkflowExecutionId = executionId,
                    WorkflowStepId = step.Id,
                    Output = string.Empty,
                    TokensUsed = 0,
                    DurationMs = 0,
                    ExecutedAt = DateTime.UtcNow,
                    InputJson = initialInputJson,
                    OutputJson = null,
                    Status = StepStatus.Running,
                    ErrorDetails = null,
                    IterationNumber = iterationCount,
                    CompletedAt = null,
                    OutputStorageKey = null
                };
                db.WorkflowStepResults.Add(stepResult);
                await db.SaveChangesAsync(ct);

                // Wire the ephemeral progress-reporting hook now that stepResult.Id exists — see
                // StepExecutionContext.ReportProgressAsync's doc comment. Capturing `stepResult`
                // by reference here is safe even though its fields mutate below: WorkflowStepProgress
                // only ever reads stepResult.Id, which is fixed at creation.
                context.StepResultId = stepResult.Id;
                context.ProgressReporter = (stage, percent, progressCt) =>
                    _eventPublisher.PublishStepProgressAsync(execution, step, stepResult, stage, percent, progressCt);
                context.ChatTurnReporter = (turnIndex, totalTurns, speaker, speakerRole, text, idsMentioned, chatCt) =>
                    _eventPublisher.PublishStepChatTurnAsync(
                        execution, step, stepResult, turnIndex, totalTurns, speaker, speakerRole, text, idsMentioned, chatCt);

                await _eventPublisher.PublishStepStartedAsync(
                    execution,
                    step,
                    stepResult,
                    CreateLogPreview(initialInputJson, 800),
                    ct);

                StepExecutionResult result;
                try
                {
                    result = await ExecuteStepWithRetryAsync(executor, context, step, ct);
                }
                catch (Exception ex)
                {
                    result = BuildFailureStepResult(step, context, ex);

                    stepResult.Output = result.Output;
                    stepResult.TokensUsed = result.TokensUsed;
                    stepResult.DurationMs = result.DurationMs;
                    stepResult.InputJson = ResolveInputJsonForPersistence(context.LastResolvedAgentInput, accumulatedOutput);
                    stepResult.OutputJson = EnsureJsonForJsonbColumn(result.Output);
                    stepResult.Status = StepStatus.Failed;
                    stepResult.ErrorDetails = result.ErrorDetails;
                    stepResult.IterationNumber = result.IterationNumber;
                    stepResult.CompletedAt = DateTime.UtcNow;
                    stepResult.OutputStorageKey = result.OutputStorageKey;
                    stepResult.ArtifactStorageKey = result.ArtifactStorageKey;

                    await db.SaveChangesAsync(ct);
                    await _eventPublisher.PublishStepCompletedAsync(execution, step, stepResult, result, ct);
                    await _eventPublisher.PublishStepDiagnosticsAsync(execution, step, stepResult, result, ct);
                    throw;
                }

                _logger.LogInformation(
                    "Step {StepOrder} ({StepType}) completed for execution {ExecutionId} with status {Status}; duration {DurationMs}ms; tokens {TokensUsed}; nextStepIndex {NextStepIndex}",
                    step.StepOrder,
                    step.StepType,
                    executionId,
                    result.Status,
                    result.DurationMs,
                    result.TokensUsed,
                    result.NextStepIndex);

                string? inputJsonForPersistence = ResolveInputJsonForPersistence(context.LastResolvedAgentInput, accumulatedOutput);
                string outputJsonForPersistence = result.Output;

                _logger.LogDebug(
                    "Persisting step result for execution {ExecutionId}, step {StepOrder} ({StepType}). InputJsonValid={InputJsonValid}, OutputJsonValid={OutputJsonValid}, InputPreview={InputPreview}, OutputPreview={OutputPreview}",
                    executionId,
                    step.StepOrder,
                    step.StepType,
                    IsValidJson(inputJsonForPersistence),
                    IsValidJson(outputJsonForPersistence),
                    CreateLogPreview(inputJsonForPersistence, 250),
                    CreateLogPreview(outputJsonForPersistence, 250));

                // Persist final state to the previously inserted running step row
                stepResult.Output = result.Output;
                stepResult.TokensUsed = result.TokensUsed;
                stepResult.DurationMs = result.DurationMs;
                stepResult.InputJson = inputJsonForPersistence;
                stepResult.OutputJson = EnsureJsonForJsonbColumn(outputJsonForPersistence);
                stepResult.Status = result.Status;
                stepResult.ErrorDetails = result.ErrorDetails;
                stepResult.IterationNumber = result.IterationNumber;
                stepResult.CompletedAt = DateTime.UtcNow;
                stepResult.OutputStorageKey = result.OutputStorageKey;
                stepResult.ArtifactStorageKey = result.ArtifactStorageKey;

                // Handle review scores for ReviewLoop steps
                if (step.StepType == StepType.ReviewLoop && result.IterationNumber.HasValue)
                {
                    int score = ReviewLoopStepExecutor.ParseReviewScore(result.Output);
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

                // Capture/clear pending loop-back review feedback (see the declaration above the
                // while loop). Must run for every ReviewLoop step regardless of outcome so a pass
                // (or exhausted MaxIterations) clears any window left over from an earlier loop.
                if (step.StepType == StepType.ReviewLoop)
                {
                    if (result.NextStepIndex <= currentStepIndex)
                    {
                        pendingReviewFeedback = ReviewLoopStepExecutor.ExtractFeedbackSummary(result.Output);
                        pendingReviewFeedbackMinStepOrder = steps[result.NextStepIndex].StepOrder;
                        pendingReviewFeedbackMaxStepOrderExclusive = step.StepOrder;
                    }
                    else
                    {
                        pendingReviewFeedback = null;
                        pendingReviewFeedbackMinStepOrder = int.MaxValue;
                        pendingReviewFeedbackMaxStepOrderExclusive = int.MinValue;
                    }
                }

                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed persisting step result for execution {ExecutionId}, step {StepOrder} ({StepType}). InputJsonValid={InputJsonValid}, OutputJsonValid={OutputJsonValid}, InputPreview={InputPreview}, OutputPreview={OutputPreview}",
                        executionId,
                        step.StepOrder,
                        step.StepType,
                        IsValidJson(inputJsonForPersistence),
                        IsValidJson(outputJsonForPersistence),
                        CreateLogPreview(inputJsonForPersistence, 250),
                        CreateLogPreview(outputJsonForPersistence, 250));
                    throw;
                }
                await _eventPublisher.PublishStepCompletedAsync(execution, step, stepResult, result, ct);
                await _eventPublisher.PublishStepDiagnosticsAsync(execution, step, stepResult, result, ct);

                ReelForgeDiagnostics.StepDuration.Record(result.DurationMs,
                    new KeyValuePair<string, object?>("step.type", step.StepType.ToString()),
                    new KeyValuePair<string, object?>("agent.type", step.AgentDefinition.AgentType.ToString()));

                accumulatedOutput = result.Output;
                if (!string.IsNullOrEmpty(result.Output))
                {
                    string stepLabel = string.IsNullOrWhiteSpace(step.Label)
                        ? step.AgentDefinition.Name
                        : step.Label;
                    stepOutputHistory.Add(new StepOutputHistoryEntry(
                        step.StepOrder, stepLabel, result.Output, result.OutputStorageKey, result.ArtifactStorageKey));
                }
                // Prune stale history on loop-back (belt-and-braces alongside the
                // LastOrDefault/greatest-StepOrder fixes at each StepOutputHistory consumer): a
                // ReviewLoop step rewinding execution re-executes every step from the loop
                // target through (but not including) itself, which is about to append a second
                // entry for each of those StepOrders. Drop the now-stale entries up front so
                // history keeps its "each StepOrder appears at most once, always the freshest"
                // invariant true for any future consumer, not just the ones already hardened to
                // pick the latest match themselves.
                if (result.NextStepIndex <= currentStepIndex)
                {
                    int loopTargetStepOrder = steps[result.NextStepIndex].StepOrder;
                    stepOutputHistory.RemoveAll(h => h.StepOrder >= loopTargetStepOrder);
                }

                iterationCount = result.NewIterationCount;
                currentStepIndex = result.NextStepIndex;

                execution.IterationCount = iterationCount;
            }

            await EnsureAuthorArtifactProducedAsync(execution.Id, steps, db, ct);

            // Defense in depth, on top of the ExecutionCancellationRegistry fix (see that class's
            // doc comment for the actual root cause this whole mechanism guards against): even
            // with the token now genuinely reaching a concurrent CancelExecutionAsync call, a stop
            // request racing the exact moment the last step naturally finishes could still slip
            // through without ever causing an awaited call to throw. Check the flag explicitly
            // here, at the one place a "successful" completion is about to be written, so a
            // request to stop always wins regardless of whether anything deeper in the call chain
            // happened to observe and throw on it in time.
            if (ct.IsCancellationRequested)
            {
                await MarkCancelledAsync(execution, db, executionId);
                return;
            }

            execution.Status = ExecutionStatus.Passed;
            execution.ResultJson = EnsureJsonForJsonbColumn(accumulatedOutput);
            execution.CompletedAt = DateTime.UtcNow;
            execution.CurrentStepId = null;
            _logger.LogDebug(
                "Persisting execution completion for execution {ExecutionId}. ResultJsonValid={ResultJsonValid}, ResultPreview={ResultPreview}",
                executionId,
                IsValidJson(execution.ResultJson),
                CreateLogPreview(execution.ResultJson, 250));

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(
                    ex,
                    "Failed persisting execution completion for execution {ExecutionId}. ResultJsonValid={ResultJsonValid}, ResultPreview={ResultPreview}",
                    executionId,
                    IsValidJson(execution.ResultJson),
                    CreateLogPreview(execution.ResultJson, 250));
                throw;
            }
            await _eventPublisher.PublishExecutionCompletedAsync(execution, ct);

            ReelForgeDiagnostics.CompletedWorkflows.Add(1,
                new KeyValuePair<string, object?>("status", "passed"));

            _logger.LogInformation("Workflow execution {ExecutionId} completed successfully", executionId);
            _logger.LogInformation(
                "Execution {ExecutionId} summary: FinalStatus={Status}, Iterations={IterationCount}, StepOutputsRecorded={StepOutputCount}",
                executionId,
                execution.Status,
                execution.IterationCount,
                stepOutputHistory.Count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await MarkCancelledAsync(execution, db, executionId);
            // do not rethrow; cancellation is expected
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow execution {ExecutionId} failed", executionId);
            execution.Status = ExecutionStatus.Failed;
            execution.ErrorMessage = ex.Message;
            execution.CompletedAt = DateTime.UtcNow;
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (Exception saveEx)
            {
                // db may still be tracking an entity whose SaveChangesAsync already failed once
                // in this request (e.g. a step result — EF does not roll back the change tracker
                // on a failed save), so retrying on the same context can reproduce the exact same
                // failure forever, and the execution's Status update is lost with it — permanently
                // "Running" even though it has, in fact, failed. Fall back to a fresh scope/
                // DbContext touching only the execution row, decoupled from whatever the shared
                // context is still holding onto (found by e2e QA).
                _logger.LogError(saveEx,
                    "Failed to persist failure state for execution {ExecutionId} via the primary " +
                    "DbContext; retrying with a fresh scope", executionId);
                using IServiceScope failureScope = _scopeFactory.CreateScope();
                WorkflowEngineDbContext failureDb = failureScope.ServiceProvider.GetRequiredService<WorkflowEngineDbContext>();
                WorkflowExecution? freshExecution = await failureDb.WorkflowExecutions
                    .FirstOrDefaultAsync(e => e.Id == executionId, CancellationToken.None);
                if (freshExecution != null)
                {
                    freshExecution.Status = ExecutionStatus.Failed;
                    freshExecution.ErrorMessage = ex.Message;
                    freshExecution.CompletedAt = DateTime.UtcNow;
                    await failureDb.SaveChangesAsync(CancellationToken.None);
                }
            }
            await _eventPublisher.PublishExecutionFailedAsync(execution, ct);

            ReelForgeDiagnostics.CompletedWorkflows.Add(1,
                new KeyValuePair<string, object?>("status", "failed"));

            throw;
        }
        finally
        {
            ReelForgeDiagnostics.ActiveWorkflows.Add(-1);
            _cancellationRegistry.Remove(executionId);
        }
    }

    /// <summary>
    /// Persists a Cancelled result and publishes it as a failure (existing consumers treat the two
    /// the same way). Shared by the two places <see cref="ExecuteAsync"/> can discover a stop
    /// request was honored: an <see cref="OperationCanceledException"/> actually observed and
    /// thrown from within the step-execution loop, and the defensive check right before writing a
    /// "successful" completion — for exactly why that second path exists and is not redundant, see
    /// the comment at its call site.
    /// </summary>
    private async Task MarkCancelledAsync(WorkflowExecution execution, WorkflowEngineDbContext db, Guid executionId)
    {
        _logger.LogInformation("Workflow execution {ExecutionId} was cancelled", executionId);
        execution.Status = ExecutionStatus.Cancelled;
        execution.ErrorMessage = "Cancelled by user request";
        execution.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
        // publish as failed so existing consumers treat it similarly
        await _eventPublisher.PublishExecutionFailedAsync(execution, CancellationToken.None);

        ReelForgeDiagnostics.CompletedWorkflows.Add(1,
            new KeyValuePair<string, object?>("status", "cancelled"));
    }

    /// <summary>
    /// Cancels a running or queued execution. If the execution is currently
    /// being processed the associated cancellation token will be triggered.
    /// The database record is also updated to <c>Cancelled</c> when applicable.
    /// </summary>
    public async Task CancelExecutionAsync(Guid executionId, Guid requestedByUserId)
    {
        _logger.LogInformation("Cancellation requested for execution {ExecutionId} by user {UserId}", executionId, requestedByUserId);

        // cancel any running token
        if (_cancellationRegistry.TryCancel(executionId))
        {
            _logger.LogInformation("Triggered cancellation token for running execution {ExecutionId}", executionId);
        }

        // if we have no scope factory (e.g. running in unit tests) just return
        if (_scopeFactory == null)
            return;

        using IServiceScope scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowEngineDbContext>();
        var execution = await db.WorkflowExecutions.FindAsync(executionId);
        if (execution == null)
        {
            _logger.LogWarning("Cancellation requested for unknown execution {ExecutionId}", executionId);
            return;
        }

        if (execution.Status == ExecutionStatus.Queued)
        {
            // attempt to remove the pending message from RabbitMQ so it won't be
            // delivered later. this is a best-effort operation; if the message has
            // already been delivered to the engine it won't be found.
            bool removed = await _rabbitHelper.RemoveExecutionMessageAsync(executionId);
            if (removed)
            {
                _logger.LogInformation("Removed queued message for cancelled execution {ExecutionId}", executionId);
            }
            else
            {
                _logger.LogDebug("No queued message found for execution {ExecutionId} during cancellation", executionId);
            }
        }

        if (execution.Status == ExecutionStatus.Queued || execution.Status == ExecutionStatus.Running)
        {
            ExecutionStatus previousStatus = execution.Status;
            execution.Status = ExecutionStatus.Cancelled;
            execution.ErrorMessage = $"Stopped by user {requestedByUserId}";
            execution.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            _logger.LogInformation(
                "Execution {ExecutionId} marked as cancelled (previous status: {PreviousStatus})",
                executionId,
                previousStatus);
        }
        else
        {
            _logger.LogInformation(
                "Cancellation request ignored for execution {ExecutionId} because current status is {Status}",
                executionId,
                execution.Status);
        }
    }

    internal async Task<StepExecutionResult> ExecuteStepWithRetryAsync(
        IStepExecutor executor,
        StepExecutionContext context,
        WorkflowStep step,
        CancellationToken ct)
    {
        int maxRetries = ResolveMaxRetries(step);
        int attemptNumber = 0;
        Exception? lastException = null;

        while (attemptNumber < maxRetries)
        {
            attemptNumber++;
            _logger.LogInformation(
                "Executing step {StepOrder} ({StepType}) for execution {ExecutionId}, attempt {Attempt}/{MaxAttempts}",
                step.StepOrder,
                step.StepType,
                context.Execution.Id,
                attemptNumber,
                maxRetries);
            try
            {
                StepExecutionResult result = await executor.ExecuteAsync(context);

                // Check if step failed
                if (result.Status == StepStatus.Failed)
                {
                    if (attemptNumber < maxRetries)
                    {
                        if (_hardeningOptions.EnableStructuredRetryDiagnostics)
                            context.RecordRetryFeedback(attemptNumber, BuildRetryDiagnosticMessage(result.ErrorDetails));
                        else
                            context.RecordRetryFeedback(attemptNumber, result.ErrorDetails ?? "Step returned failed status without details.");

                        double delaySeconds = Math.Pow(_hardeningOptions.RetryBaseDelaySeconds, attemptNumber);
                        _logger.LogWarning(
                            "Step {StepOrder} ({StepType}) failed on attempt {Attempt}/{Max}. Error: {Error}. Retrying in {Delay}s...",
                            step.StepOrder, step.StepType, attemptNumber, maxRetries,
                            result.ErrorDetails, delaySeconds);

                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
                        continue;
                    }
                    else
                    {
                        // Max retries exceeded - throw to trigger workflow failure
                        throw new InvalidOperationException(
                            $"Step {step.StepOrder} ({step.StepType}) failed after {maxRetries} attempts. Last error: {result.ErrorDetails}");
                    }
                }

                // Success - return result
                if (attemptNumber > 1)
                {
                    _logger.LogInformation(
                        "Step {StepOrder} ({StepType}) succeeded on attempt {Attempt}",
                        step.StepOrder, step.StepType, attemptNumber);
                }
                return WithAttemptMetadata(result, attemptNumber);
            }
            // do not retry on InvalidOperationException or our workflow‑abort exception
            catch (AgentWorkflowException awf)
            {
                // agent explicitly requested workflow abort; log then rethrow immediately
                _logger.LogInformation(
                    "Step {StepOrder} invoked FailWorkflow: {Reason}",
                    step.StepOrder, awf.Reason);
                throw;
            }
            catch (Exception ex) when (ex is not InvalidOperationException && ex is not AgentWorkflowException && attemptNumber < maxRetries)
            {
                lastException = ex;
                context.RecordRetryFeedback(attemptNumber, BuildRetryDiagnosticMessage(ex.Message));

                double delaySeconds = Math.Pow(_hardeningOptions.RetryBaseDelaySeconds, attemptNumber);
                _logger.LogWarning(ex,
                    "Step {StepOrder} ({StepType}) threw exception on attempt {Attempt}/{Max}. Retrying in {Delay}s...",
                    step.StepOrder, step.StepType, attemptNumber, maxRetries, delaySeconds);

                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
            }
        }

        // If we get here, all retries are exhausted
        throw new InvalidOperationException(
            $"Step {step.StepOrder} ({step.StepType}) failed after {maxRetries} attempts",
            lastException);
    }

    private int ResolveMaxRetries(WorkflowStep step)
    {
        if (step.StepType == StepType.Extract
            || step.StepType == StepType.VideoAnalyze
            || step.StepType == StepType.VideoCompile
            || step.StepType == StepType.EditRoom)
        {
            // Deterministic, non-LLM steps: retrying the whole step reproduces the same failure
            // (Extract) or re-burns minutes of ffmpeg decode/encode to reproduce a deterministic
            // failure (VideoAnalyze/VideoCompile). ASR's own network call inside
            // VideoAnalyzeStepExecutor has its own small bounded retry around just that call —
            // it does not go through this outer step-level retry mechanism. EditRoom is NOT
            // deterministic (it makes many LLM calls), but an outer retry re-running the whole
            // room from scratch is expensive — the step's own internal synthesis retry
            // (EditRoomStepConfig.MaxSynthesisAttempts) is where retry value actually is.
            return 1;
        }

        int configuredDefault = Math.Clamp(_hardeningOptions.MaxStepRetries, 1, 6);
        AgentType? agentType = step.AgentDefinition?.AgentType;

        return agentType switch
        {
            AgentType.AuthorAgent => Math.Clamp(_hardeningOptions.MaxAuthorStepRetries, 1, 6),
            AgentType.RemotionComponentTranslator => Math.Clamp(_hardeningOptions.MaxTranslatorStepRetries, 1, 6),
            _ => configuredDefault
        };
    }

    private static StepExecutionResult BuildFailureStepResult(WorkflowStep step, StepExecutionContext context, Exception ex)
    {
        string diagnostic = BuildRetryDiagnosticMessage(ex.Message);
        return new StepExecutionResult
        {
            // BuildRetryDiagnosticMessage returns PLAIN TEXT, not JSON — and this Output value
            // is written verbatim into WorkflowStepResult.OutputJson, a jsonb column. Writing
            // plain text there fails the whole SaveChangesAsync with Postgres error 22P02
            // ("invalid input syntax for type json"), which then gets retried at the message-bus
            // level while the execution is already marked Running — leaving it stuck forever.
            // This is the single most common way to reach this method: any step whose
            // ResolveMaxRetries is 1 (Extract, VideoAnalyze, VideoCompile) throws here on its
            // very first failure, discarding its own already-valid-JSON failure envelope. Wrap
            // the diagnostic in a minimal JSON envelope so persistence never crashes.
            Output = JsonSerializer.Serialize(new { status = "failed", error = new { message = diagnostic } }),
            NextStepIndex = context.CurrentStepIndex + 1,
            NewIterationCount = context.IterationCount,
            DurationMs = 0,
            TokensUsed = 0,
            Status = StepStatus.Failed,
            ErrorDetails = ex.Message,
            AttemptCount = 1,
            RetryCount = 0
        };
    }

    private static StepExecutionResult WithAttemptMetadata(StepExecutionResult result, int attemptCount)
    {
        return new StepExecutionResult
        {
            Output = result.Output,
            NextStepIndex = result.NextStepIndex,
            NewIterationCount = result.NewIterationCount,
            TokensUsed = result.TokensUsed,
            InputTokens = result.InputTokens,
            OutputTokens = result.OutputTokens,
            DurationMs = result.DurationMs,
            AttemptCount = attemptCount,
            RetryCount = Math.Max(0, attemptCount - 1),
            Status = result.Status,
            ErrorDetails = result.ErrorDetails,
            IterationNumber = result.IterationNumber,
            OutputStorageKey = result.OutputStorageKey,
            // Easy to miss (plan R3): this method hand-copies every field of the attempt's
            // result — anything added to StepExecutionResult but not copied here is silently
            // dropped on any step that goes through a retry attempt.
            ArtifactStorageKey = result.ArtifactStorageKey
        };
    }

    private static string BuildRetryDiagnosticMessage(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "Step execution failed without diagnostics.";

        string[] lines = raw
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line =>
                line.Contains("error", StringComparison.OrdinalIgnoreCase)
                || line.Contains("Cannot", StringComparison.OrdinalIgnoreCase)
                || line.Contains("failed", StringComparison.OrdinalIgnoreCase)
                || line.Contains("TS", StringComparison.Ordinal))
            .Take(8)
            .ToArray();

        return lines.Length == 0
            ? raw.Trim()
            : string.Join("\n", lines);
    }

    internal static string? ResolveInputJsonForPersistence(string? resolvedAgentInput, string accumulatedOutput)
    {
        if (!string.IsNullOrWhiteSpace(resolvedAgentInput))
            return resolvedAgentInput;

        return string.IsNullOrWhiteSpace(accumulatedOutput) ? null : accumulatedOutput;
    }

    /// <summary>
    /// True when <paramref name="stepOrder"/> falls in the half-open window
    /// <c>[minStepOrderInclusive, maxStepOrderExclusive)</c> a ReviewLoop step's loop-back opened —
    /// i.e. the loop target through (but not including) the ReviewLoop step itself. Extracted as a
    /// pure, directly-testable helper from the main execution loop's inline condition (see
    /// <see cref="ExecuteAsync"/>'s <c>pendingReviewFeedback</c> bookkeeping).
    /// </summary>
    internal static bool IsWithinReviewFeedbackWindow(int stepOrder, int minStepOrderInclusive, int maxStepOrderExclusive) =>
        stepOrder >= minStepOrderInclusive && stepOrder < maxStepOrderExclusive;

    private static async Task EnsureAuthorArtifactProducedAsync(
        Guid executionId,
        IReadOnlyCollection<WorkflowStep> workflowSteps,
        WorkflowEngineDbContext db,
        CancellationToken ct)
    {
        if (!workflowSteps.Any(step => step.AgentDefinition.AgentType == AgentType.AuthorAgent))
            return;

        HashSet<Guid> authorStepIds = workflowSteps
            .Where(step => step.AgentDefinition.AgentType == AgentType.AuthorAgent)
            .Select(step => step.Id)
            .ToHashSet();

        List<string?> outputStorageKeys = await db.WorkflowStepResults
            .AsNoTracking()
            .Where(result => result.WorkflowExecutionId == executionId
                && result.Status == StepStatus.Completed
                && authorStepIds.Contains(result.WorkflowStepId))
            .Select(result => result.OutputStorageKey)
            .ToListAsync(ct);

        if (outputStorageKeys.Any(key => !string.IsNullOrWhiteSpace(key)))
            return;

        throw new InvalidOperationException(
            "Workflow contains an Author step but no rendered media artifact was produced. Ensure the Author step completes rendering and sets outputStorageKey.");
    }

    /// <summary>
    /// Ensures a value is safe to write to a jsonb column: null/empty or already-valid JSON pass
    /// through unchanged; anything else (e.g. a chat completion that didn't conform to its
    /// requested output schema — structured-output enforcement is a request, not a guarantee, and
    /// some OpenAI-compatible endpoints ignore it entirely) is wrapped as a JSON string. Without
    /// this, writing arbitrary text into WorkflowStepResult.OutputJson/WorkflowExecution.ResultJson
    /// (both jsonb) fails the whole SaveChangesAsync with Postgres error 22P02 ("invalid input
    /// syntax for type json") — and since the step/execution's Status change is batched in the
    /// same SaveChangesAsync call, that failure discards the Status update too, leaving the
    /// execution stuck "Running" forever (found by e2e QA).
    /// </summary>
    private static string? EnsureJsonForJsonbColumn(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        try
        {
            using JsonDocument _ = JsonDocument.Parse(value);
            return value;
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(value);
        }
    }

    private static bool IsValidJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        try
        {
            using JsonDocument _ = JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string CreateLogPreview(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string normalized = value.Replace("\r", " ").Replace("\n", " ").Trim();
        if (normalized.Length <= maxLength)
            return normalized;

        return normalized[..maxLength];
    }
}
