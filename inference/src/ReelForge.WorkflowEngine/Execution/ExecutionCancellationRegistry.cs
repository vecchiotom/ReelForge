using System.Collections.Concurrent;

namespace ReelForge.WorkflowEngine.Execution;

/// <summary>
/// Tracks the live <see cref="CancellationTokenSource"/> for each currently-running workflow
/// execution, so a stop request arriving through a completely different code path (a separate
/// MassTransit consumer, handling <c>WorkflowExecutionStopRequested</c> in its own DI scope) can
/// actually reach the token the in-flight <see cref="WorkflowExecutorService.ExecuteAsync"/> call
/// is honoring.
///
/// <para>
/// Registered as a singleton deliberately: <see cref="WorkflowExecutorService"/> itself is Scoped
/// (it depends on <c>IWorkflowEventPublisher</c>, which is itself Scoped, so making the service a
/// singleton would create a captive-dependency bug) — one instance per consumed message, each with
/// its own fields. Before this was pulled out, the cancellation-token dictionary lived directly on
/// <see cref="WorkflowExecutorService"/> as an instance field, so the instance handling a
/// long-running execution and the instance handling a concurrent stop request for that SAME
/// execution never shared the same dictionary: the stop request would find nothing to cancel, but
/// still flip the execution's database row to <c>Cancelled</c> — which then got silently
/// overwritten back to <c>Passed</c>/<c>Failed</c> once the (never actually cancelled) execution
/// finished naturally on its own, minutes or tens of minutes later. Verified live: a stop request
/// issued while a slow reasoning-model Agent step was mid-flight had no effect at all, and the
/// execution went on to complete successfully ~37 minutes afterward.
/// </para>
/// </summary>
public sealed class ExecutionCancellationRegistry
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _tokens = new();

    /// <summary>
    /// Why each cancelled execution was cancelled, recorded by whoever pulled the trigger.
    ///
    /// <para>
    /// This exists because a <see cref="CancellationToken"/> carries no provenance: by the time
    /// <see cref="WorkflowExecutorService.ExecuteAsync"/> catches an
    /// <see cref="OperationCanceledException"/>, "the user pressed Stop", "the RabbitMQ broker
    /// force-closed the channel because we had not acked within consumer_timeout", and "the
    /// process is shutting down" are completely indistinguishable. The executor used to resolve
    /// that ambiguity by simply asserting the first one — writing the literal string
    /// <c>"Cancelled by user request"</c> onto the execution row for ALL of them. That was wrong
    /// often enough to actively mislead: a transport-level kill nobody asked for would be
    /// reported to the user as their own doing, sending every subsequent investigation looking
    /// for a phantom Stop click instead of at the broker. Recording the reason at the point of
    /// cancellation is the only place the truth is actually known.
    /// </para>
    /// </summary>
    private readonly ConcurrentDictionary<Guid, string> _reasons = new();

    /// <summary>
    /// The message written onto an execution cancelled without any recorded reason. Deliberately
    /// phrased as "the engine does not know", because that is the honest state: every code path
    /// that cancels on purpose records why, so reaching this string means the token was tripped by
    /// something ambient (transport, host shutdown, a linked token upstream) rather than by intent.
    /// Never replace this with a user-blaming default — see the <see cref="_reasons"/> doc comment.
    /// </summary>
    public const string UnknownReason =
        "Cancelled by the workflow engine (no stop request was recorded — most likely the process " +
        "shut down or the message transport dropped the delivery mid-execution).";

    /// <summary>Registers the token source for a starting execution. Overwrites any prior entry for the same id (there should never be one — see the "only Queued executions are eligible to start" guard in ExecuteAsync).</summary>
    public void Register(Guid executionId, CancellationTokenSource cts) => _tokens[executionId] = cts;

    /// <summary>Removes the entry once the execution has finished (success, failure, or cancellation) — called from ExecuteAsync's outer finally block.</summary>
    public void Remove(Guid executionId)
    {
        _tokens.TryRemove(executionId, out _);
        _reasons.TryRemove(executionId, out _);
    }

    /// <summary>
    /// Cancels the token for a running execution, if one is currently registered, recording
    /// <paramref name="reason"/> as the cause so the executor can report it accurately instead of
    /// guessing. Returns false (a no-op, not an error) when the execution isn't in this registry —
    /// e.g. it's Queued but not yet dispatched, or it already finished — callers fall back to a
    /// database-only status update for those cases.
    /// </summary>
    /// <param name="executionId">The execution to cancel.</param>
    /// <param name="reason">
    /// A human-readable cause, persisted verbatim onto the execution row. The reason is recorded
    /// even when no live token is found, so a stop that races an execution's startup still
    /// attributes correctly.
    /// </param>
    public bool TryCancel(Guid executionId, string reason)
    {
        if (!string.IsNullOrWhiteSpace(reason))
            _reasons[executionId] = reason;

        if (_tokens.TryGetValue(executionId, out CancellationTokenSource? cts))
        {
            cts.Cancel();
            return true;
        }

        return false;
    }

    /// <summary>
    /// The recorded cause for an execution's cancellation, or <see cref="UnknownReason"/> when
    /// nothing recorded one — which, as that constant's doc comment explains, is itself the
    /// meaningful signal that no deliberate stop happened.
    /// </summary>
    public string GetReason(Guid executionId) =>
        _reasons.TryGetValue(executionId, out string? reason) ? reason : UnknownReason;

    /// <summary>Test/diagnostic hook — whether an execution currently has a registered, live token.</summary>
    public bool Contains(Guid executionId) => _tokens.ContainsKey(executionId);
}
