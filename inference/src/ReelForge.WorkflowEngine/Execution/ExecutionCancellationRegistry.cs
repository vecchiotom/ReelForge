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

    /// <summary>Registers the token source for a starting execution. Overwrites any prior entry for the same id (there should never be one — see the "only Queued executions are eligible to start" guard in ExecuteAsync).</summary>
    public void Register(Guid executionId, CancellationTokenSource cts) => _tokens[executionId] = cts;

    /// <summary>Removes the entry once the execution has finished (success, failure, or cancellation) — called from ExecuteAsync's outer finally block.</summary>
    public void Remove(Guid executionId) => _tokens.TryRemove(executionId, out _);

    /// <summary>
    /// Cancels the token for a running execution, if one is currently registered. Returns false
    /// (a no-op, not an error) when the execution isn't in this registry — e.g. it's Queued but not
    /// yet dispatched, or it already finished — callers fall back to a database-only status update
    /// for those cases.
    /// </summary>
    public bool TryCancel(Guid executionId)
    {
        if (_tokens.TryGetValue(executionId, out CancellationTokenSource? cts))
        {
            cts.Cancel();
            return true;
        }

        return false;
    }

    /// <summary>Test/diagnostic hook — whether an execution currently has a registered, live token.</summary>
    public bool Contains(Guid executionId) => _tokens.ContainsKey(executionId);
}
