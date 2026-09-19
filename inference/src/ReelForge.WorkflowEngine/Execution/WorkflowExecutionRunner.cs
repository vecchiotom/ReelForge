using System.Collections.Concurrent;

namespace ReelForge.WorkflowEngine.Execution;

/// <summary>
/// Runs workflow executions on background tasks whose lifetime is bounded only by the process,
/// decoupling how long an execution takes from how long the message transport is willing to wait
/// for its delivery to be acknowledged.
///
/// <para><b>Why this type exists.</b>
/// <c>WorkflowExecutionRequestedConsumer</c> used to <c>await</c> the entire workflow inline, so
/// the RabbitMQ delivery stayed unacknowledged for the execution's full duration — hours, for a
/// code-analysis pipeline driving a local reasoning model. RabbitMQ's <c>consumer_timeout</c>
/// force-closes the channel of any delivery not acked within its window, which MassTransit
/// surfaces by cancelling <c>ConsumeContext.CancellationToken</c>; that token was the very one
/// handed to <c>ExecuteAsync</c> as its cancellation signal, so the broker's ack deadline silently
/// doubled as a hard wall-clock cap on every workflow. Observed live and reproduced from the
/// execution table: three consecutive <c>lean-context-promo</c> runs died at
/// <b>exactly 120.0 minutes</b> — <c>consumer_timeout = 7200000</c> ms to the second — each
/// discarding 40+ minutes of completed analysis steps and each mislabelled
/// "Cancelled by user request" despite nobody touching Stop.
/// </para>
///
/// <para>
/// Raising <c>consumer_timeout</c> (which is what the previous round of this bug did, from the
/// stock 30 minutes to 2 hours) only moves the wall. Any value is eventually exceeded by a
/// legitimately long execution, and exceeding it destroys work rather than degrading. The ack
/// window and the execution's duration have to stop being the same number, which is what this
/// runner does: the consumer hands the execution over, returns, and the delivery is acked within
/// milliseconds, while the work continues here.
/// </para>
///
/// <para><b>Where the concurrency limit lives now.</b>
/// Acking immediately means MassTransit's <c>ConcurrentMessageLimit</c> no longer bounds how many
/// executions run at once — it only bounds how many are being handed over at once. The real limit
/// moved to <see cref="_slots"/>, and it is acquired <i>in the consumer</i>, before dispatch,
/// deliberately: a consumer blocked waiting for a slot is holding an unacked delivery again, but
/// it is holding one that has performed <i>no work at all</i>, so if the broker does kill it the
/// redelivery costs nothing and the execution (still <c>Queued</c> in the database) simply starts
/// later. That is the one shape of this problem that is safe to have.
/// </para>
///
/// <para><b>Crash semantics.</b>
/// Because the delivery is acked while the execution is still running, a hard process kill leaves
/// the execution row <c>Running</c> with no message to redeliver, instead of the pre-change
/// behaviour where the unacked message came back. That trade is accepted: the redelivery it
/// replaces was almost always a no-op anyway, since <c>ExecuteAsync</c> refuses to start anything
/// not in <c>Queued</c> status, so a redelivered message for a crashed mid-flight execution was
/// already discarded on arrival rather than resuming it. Graceful shutdown is handled properly —
/// see <see cref="StopAsync"/>.
/// </para>
/// </summary>
public sealed class WorkflowExecutionRunner : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WorkflowExecutionRunner> _logger;
    private readonly SemaphoreSlim _slots;

    /// <summary>
    /// Ties every in-flight execution to the process lifetime, and <i>only</i> to it. This is the
    /// token <see cref="WorkflowExecutorService.ExecuteAsync"/> now receives, replacing the
    /// transport's <c>ConsumeContext.CancellationToken</c> — the substitution that actually fixes
    /// the bug in this type's summary, since nothing about a broker ack deadline can reach it.
    /// </summary>
    private readonly CancellationTokenSource _lifetimeCts = new();

    /// <summary>In-flight executions, so <see cref="StopAsync"/> can wait for them rather than tearing them down mid-step.</summary>
    private readonly ConcurrentDictionary<Guid, Task> _running = new();

    public WorkflowExecutionRunner(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<WorkflowExecutionRunner> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        int maxConcurrency = Math.Max(1, configuration.GetValue("WorkflowEngine:MaxConcurrency", 4));
        _slots = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        _logger.LogInformation(
            "WorkflowExecutionRunner initialised with MaxConcurrency={MaxConcurrency}", maxConcurrency);
    }

    /// <summary>Number of executions currently running. Test/diagnostic hook.</summary>
    public int RunningCount => _running.Count;

    /// <summary>
    /// Acquires a concurrency slot (honouring <paramref name="admissionToken"/>, which is the
    /// consumer's transport token — see the class summary for why blocking here specifically is
    /// the safe place to block) and then starts the execution on a background task bound to the
    /// process lifetime. Returns as soon as the execution has started, so the caller can
    /// acknowledge its message.
    /// </summary>
    public async Task DispatchAsync(Guid executionId, string correlationId, CancellationToken admissionToken)
    {
        await _slots.WaitAsync(admissionToken);

        // From here on the slot is owned by the background task, which releases it in its finally.
        // Anything that can throw between the Wait above and the Task.Run below must release it
        // first, hence the try/catch rather than a bare launch.
        try
        {
            Task execution = Task.Run(
                () => RunAsync(executionId, correlationId),
                CancellationToken.None);

            _running[executionId] = execution;

            // Detached on purpose: the whole point is that the caller (and therefore the message
            // acknowledgement) does not wait for this. RunAsync never throws — it funnels every
            // failure into logging — so this cannot become an unobserved faulted task.
            _ = execution.ContinueWith(
                _ => _running.TryRemove(executionId, out _),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        catch
        {
            _slots.Release();
            throw;
        }
    }

    private async Task RunAsync(Guid executionId, string correlationId)
    {
        try
        {
            // A scope per execution: WorkflowExecutorService and IWorkflowEventPublisher are both
            // Scoped, and the consumer's own scope is gone the moment it acks and returns, so the
            // runner must own the scope for as long as the execution does.
            using IServiceScope scope = _scopeFactory.CreateScope();
            WorkflowExecutorService executor = scope.ServiceProvider.GetRequiredService<WorkflowExecutorService>();

            await executor.ExecuteAsync(executionId, correlationId, _lifetimeCts.Token);

            _logger.LogInformation(
                "Workflow execution {ExecutionId} finished (CorrelationId={CorrelationId})",
                executionId, correlationId);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "Workflow execution {ExecutionId} was cancelled while running in the background", executionId);
        }
        catch (Exception ex)
        {
            // ExecuteAsync has already persisted the failure and published the failure event
            // before rethrowing; there is no message left to nack (it was acked at dispatch), so
            // logging is genuinely the whole remaining obligation here.
            _logger.LogError(ex, "Workflow execution {ExecutionId} failed", executionId);
        }
        finally
        {
            _slots.Release();
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// On shutdown, signals in-flight executions and gives them the host's shutdown window to
    /// unwind — which lets each one persist a Cancelled status carrying an accurate reason
    /// (<see cref="ExecutionCancellationRegistry"/>) instead of vanishing mid-step and leaving a
    /// row stuck at <c>Running</c> forever.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task[] inFlight = _running.Values.ToArray();
        if (inFlight.Length == 0)
        {
            await _lifetimeCts.CancelAsync();
            return;
        }

        _logger.LogInformation(
            "WorkflowEngine is shutting down with {Count} execution(s) in flight; signalling cancellation",
            inFlight.Length);

        foreach (Guid executionId in _running.Keys)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            scope.ServiceProvider.GetRequiredService<ExecutionCancellationRegistry>()
                .TryCancel(executionId, "Interrupted: the workflow engine shut down while this execution was running.");
        }

        await _lifetimeCts.CancelAsync();

        try
        {
            await Task.WhenAll(inFlight).WaitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Not all in-flight executions unwound before the shutdown deadline");
        }
    }
}
