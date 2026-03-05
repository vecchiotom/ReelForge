using System.Threading;

namespace ReelForge.WorkflowEngine.Execution;

public sealed class WorkflowExecutionContext
{
    public required Guid ExecutionId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string CorrelationId { get; init; }
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
