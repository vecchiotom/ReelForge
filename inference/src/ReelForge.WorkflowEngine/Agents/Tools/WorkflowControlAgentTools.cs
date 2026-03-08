using System.Threading.Tasks;

namespace ReelForge.WorkflowEngine.Agents.Tools;

/// <summary>
/// Exposes workflow‑control helpers for agents. Currently contains only <see cref="FailWorkflow"/>,
/// which throws an <see cref="AgentWorkflowException"/> to stop execution immediately.
/// </summary>
public class WorkflowControlAgentTools
{
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
        throw new AgentWorkflowException(reason);
    }
}
