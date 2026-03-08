namespace ReelForge.WorkflowEngine.Agents.Tools;

/// <summary>
/// Thrown by an agent when it needs to abort the entire workflow immediately.
/// The message / <see cref="Reason"/> will be recorded on the workflow execution
/// and surfaced to the user.
/// </summary>
public class AgentWorkflowException : Exception
{
    public AgentWorkflowException(string reason)
        : base(reason)
    {
        Reason = reason;
    }

    /// <summary>
    /// A human‑readable explanation of why the workflow failed.
    /// </summary>
    public string Reason { get; }
}
