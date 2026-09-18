namespace ReelForge.Shared.Data.Models;

/// <summary>
/// The result of executing a single workflow step.
/// </summary>
public class WorkflowStepResult
{
    public Guid Id { get; set; }
    public Guid WorkflowExecutionId { get; set; }
    public Guid WorkflowStepId { get; set; }
    public string Output { get; set; } = string.Empty;
    public int TokensUsed { get; set; }
    public long DurationMs { get; set; }
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;

    // Enhanced fields
    public string? InputJson { get; set; }
    public string? OutputJson { get; set; }
    public StepStatus Status { get; set; } = StepStatus.Pending;
    public string? ErrorDetails { get; set; }
    public int? IterationNumber { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// S3/MinIO storage key for a video or image artifact produced by this step.
    /// Null when the step produces no media output.
    /// Key format: projects/{projectId}/outputFiles/{executionId}/{fileName}
    /// </summary>
    public string? OutputStorageKey { get; set; }

    /// <summary>
    /// S3/MinIO storage key for a large, non-playable JSON artifact produced by this step (e.g.
    /// a full video analysis document, or a compile step's edit-decision-list audit trail).
    /// Deliberately a SEPARATE column from <see cref="OutputStorageKey"/>: OutputsController
    /// treats any outputFiles-prefixed OutputStorageKey as a playable video, so a JSON artifact
    /// must never land there. Null when the step produces no such artifact.
    /// Key format: projects/{projectId}/agentFiles/video-analysis/{executionId}/...
    /// </summary>
    public string? ArtifactStorageKey { get; set; }

    /// <summary>
    /// Tool calls made during this step, persisted from <c>StepExecutionResult.ToolCalls</c> at
    /// completion so reopening the execution page later shows the same history a live
    /// SSE-connected tab saw. Array of the same shape <c>WorkflowStepToolCalled</c> publishes per
    /// call (tool name, arguments, result) — see WorkflowEvents.cs. Null when no tool calls were
    /// made during this step.
    /// </summary>
    public string? ToolCallsJson { get; set; }

    /// <summary>
    /// Reasoning/thinking text captured during this step, persisted from
    /// <c>StepExecutionResult.Reasoning</c> at completion, same rationale as
    /// <see cref="ToolCallsJson"/>. Null when no reasoning text was captured during this step.
    /// </summary>
    public string? ReasoningJson { get; set; }

    /// <summary>
    /// For an EditRoom step only: the full room transcript (all turns, in order), persisted at
    /// completion so reopening the execution page shows the complete discussion immediately
    /// instead of only whatever a live SSE-connected tab happened to see. Per-turn shape mirrors
    /// <c>WorkflowStepChatTurn</c>'s fields (turnIndex, speaker, speakerRole, text, truncated,
    /// idsMentioned, totalTurns) but with the FULL untruncated text, not the ~600-char-truncated
    /// SSE broadcast version. Null for every non-EditRoom step type.
    /// </summary>
    public string? ChatTranscriptJson { get; set; }

    public WorkflowExecution WorkflowExecution { get; set; } = null!;
    public WorkflowStep WorkflowStep { get; set; } = null!;
}
