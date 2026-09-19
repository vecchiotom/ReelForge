using System.Threading.Tasks;
using ReelForge.WorkflowEngine.Execution;

namespace ReelForge.WorkflowEngine.Agents.Tools;

/// <summary>
/// Exposes workflow‑control helpers for agents. Currently contains only <see cref="FailWorkflow"/>,
/// which throws an <see cref="AgentWorkflowException"/> to stop execution immediately.
/// </summary>
public class WorkflowControlAgentTools
{
    private readonly IWorkflowExecutionContextAccessor _executionContextAccessor;
    private readonly ILogger<WorkflowControlAgentTools> _logger;

    public WorkflowControlAgentTools(
        IWorkflowExecutionContextAccessor executionContextAccessor,
        ILogger<WorkflowControlAgentTools> logger)
    {
        _executionContextAccessor = executionContextAccessor;
        _logger = logger;
    }

    /// <summary>
    /// Signals that the workflow cannot continue and must be aborted. The provided reason is
    /// stored on the workflow execution record and shown in the UI. This method never returns;
    /// it throws <see cref="AgentWorkflowException"/>. Agents should call this only when
    /// encountering an unrecoverable condition that cannot be addressed by retries or later
    /// steps. For transient errors prefer allowing the step to fail normally so the engine can
    /// retry according to its policies.
    /// </summary>
    /// <param name="reason">A clear, human-readable explanation of the failure cause.</param>
    /// <returns>Never returns; always throws.</returns>
    public Task FailWorkflow(string reason)
    {
        WorkflowExecutionContext? context = _executionContextAccessor.Current;
        _logger.LogWarning(
            "Tool call fail_workflow requested (ExecutionId={ExecutionId}, ProjectId={ProjectId}, CorrelationId={CorrelationId}, Reason={Reason})",
            context?.ExecutionId,
            context?.ProjectId,
            context?.CorrelationId,
            reason);

        // Recorded for diagnostics only -- AgentStepExecutor deliberately does not act on it.
        // See the long comment there for why enforcing this signal was tried and reverted.
        if (context is not null)
            context.AbortReason = reason;

        throw new AgentWorkflowException(reason);
    }
}
