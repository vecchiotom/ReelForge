using System.Threading;

namespace ReelForge.WorkflowEngine.Execution;

public sealed class WorkflowExecutionContext
{
    public required Guid ExecutionId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string CorrelationId { get; init; }

    /// <summary>
    /// Set by the RenderVideoAndUploadToStorage tool when it produces a video artifact.
    /// The workflow executor reads this after agent completion and persists it to WorkflowStepResult.
    /// </summary>
    public string? PendingOutputStorageKey { get; set; }

    /// <summary>
    /// Set by <c>WorkflowControlAgentTools.FailWorkflow</c> when an agent asks to abort. Recorded
    /// as STATE rather than relying solely on the <c>AgentWorkflowException</c> that tool throws,
    /// because that exception does not escape the agent run: <c>FunctionInvokingChatClient</c>
    /// catches exceptions thrown by a tool and feeds them back to the model as a tool result, so
    /// the abort signal is swallowed and the model simply keeps going.
    ///
    /// <para>
    /// Observed live: a Colorist step called fail_workflow, was handed its own exception back as a
    /// tool result, and looped read_project_file -> fail_workflow for over forty minutes without
    /// ever failing the step -- while the tool's own documentation promised it would "abort the
    /// entire workflow immediately". Checking this flag after the run is what actually honors that
    /// contract.
    /// </para>
    /// </summary>
    public string? AbortReason { get; set; }
}

public interface IWorkflowExecutionContextAccessor
{
    WorkflowExecutionContext? Current { get; }
    IDisposable BeginScope(Guid executionId, Guid projectId, string correlationId);
}

public class WorkflowExecutionContextAccessor : IWorkflowExecutionContextAccessor
{
    private readonly AsyncLocal<WorkflowExecutionContext?> _current = new();
    public WorkflowExecutionContext? Current => _current.Value;

    public IDisposable BeginScope(Guid executionId, Guid projectId, string correlationId)
    {
        WorkflowExecutionContext? previous = _current.Value;
        _current.Value = new WorkflowExecutionContext
        {
            ExecutionId = executionId,
            ProjectId = projectId,
            CorrelationId = correlationId
        };
        return new RestoreScope(() => _current.Value = previous);
    }

    private sealed class RestoreScope(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
