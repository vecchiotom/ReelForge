namespace ReelForge.Inference.Services.Background;

/// <summary>Task payload for file summarization.</summary>
public record FileSummarizationTask(Guid FileId);

/// <summary>Task payload for workflow execution.</summary>
public record WorkflowExecutionTask(Guid ExecutionId);
