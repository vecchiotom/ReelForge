namespace ReelForge.Shared.IntegrationEvents;

/// <summary>
/// Published by Inference API when user triggers workflow execution.
/// </summary>
public record WorkflowExecutionRequested
{
    public Guid ExecutionId { get; init; }
    public Guid WorkflowDefinitionId { get; init; }
    public Guid ProjectId { get; init; }
    public Guid InitiatedByUserId { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public DateTime RequestedAt { get; init; } = DateTime.UtcNow;
    /// <summary>
    /// Optional free-text user request passed as context to all agents.
    /// Null when the workflow was executed without user input.
    /// </summary>
    public string? UserRequest { get; init; }
}

/// <summary>
/// Published by WorkflowEngine on successful completion.
/// </summary>
public record WorkflowExecutionCompleted
{
    public Guid ExecutionId { get; init; }
    public Guid ProjectId { get; init; }
    public Guid? WorkflowDefinitionId { get; init; }
    public Guid? InitiatedByUserId { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public string FinalStatus { get; init; } = string.Empty;
    public string? ResultJson { get; init; }
    public DateTime CompletedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Published by WorkflowEngine when an execution transitions to Running.
/// </summary>
public record WorkflowExecutionRunning
{
    public Guid ExecutionId { get; init; }
    public Guid ProjectId { get; init; }
    public Guid? WorkflowDefinitionId { get; init; }
    public Guid? InitiatedByUserId { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Event produced when a user (or admin) requests that a running or queued
/// workflow execution be aborted. The WorkflowEngine will handle cancellation
/// and update the execution status accordingly.
/// </summary>
public record WorkflowExecutionStopRequested
{
    public Guid ExecutionId { get; init; }
    public Guid RequestedByUserId { get; init; }
    public DateTime RequestedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Published per step for real-time tracking.
/// </summary>
public record WorkflowStepCompleted
{
    public Guid ExecutionId { get; init; }
    public Guid StepId { get; init; }
    public Guid StepResultId { get; init; }
    public Guid? ProjectId { get; init; }
    public Guid? WorkflowDefinitionId { get; init; }
    public int? StepOrder { get; init; }
    public string? StepLabel { get; init; }
    public string? StepType { get; init; }
    public int? IterationNumber { get; init; }
    public string? AgentType { get; init; }
    public string? AgentName { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public string StepStatus { get; init; } = string.Empty;
    public int TokensUsed { get; init; }
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public int? AttemptCount { get; init; }
    public int? RetryCount { get; init; }
    public long DurationMs { get; init; }
    public int ToolCallCount { get; init; }
    public int ReasoningCount { get; init; }
    public string? ErrorDetails { get; init; }
    public string? OutputStorageKey { get; init; }
    public DateTime CompletedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Published as a lightweight, ephemeral progress signal while a long-running step (initially
/// <c>VideoAnalyze</c>/<c>VideoCompile</c>, but any step executor may opt in) is still executing —
/// e.g. "Downloading source", "Transcribing audio (chunk 2/5)", "Encoding" with a percent. This is
/// transient UI signal, not execution history: unlike <see cref="WorkflowStepStarted"/>/
/// <see cref="WorkflowStepCompleted"/> it is never persisted to <c>WorkflowStepResult</c> and
/// carries no EF Core migration, so consumers must treat a later progress event for the same
/// <see cref="StepResultId"/> as superseding any earlier one and must never rely on delivery —
/// <see cref="WorkflowStepCompleted"/>/<see cref="WorkflowExecutionFailed"/> remain the only
/// authoritative signal that a step is actually done.
/// </summary>
public record WorkflowStepProgress
{
    public Guid ExecutionId { get; init; }
    public Guid StepId { get; init; }
    public Guid StepResultId { get; init; }
    public Guid? ProjectId { get; init; }
    public Guid? WorkflowDefinitionId { get; init; }
    public int? StepOrder { get; init; }
    public string? StepLabel { get; init; }
    public string? StepType { get; init; }
    public string CorrelationId { get; init; } = string.Empty;

    /// <summary>Short human-readable stage label, e.g. "Downloading source" or "Encoding".</summary>
    public string Stage { get; init; } = string.Empty;

    /// <summary>0-100 when a real percentage is available (e.g. ffmpeg encode progress); null otherwise — the frontend falls back to showing just the stage label.</summary>
    public int? PercentComplete { get; init; }

    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Published when an agent invokes a tool while executing a workflow step.
/// </summary>
public record WorkflowStepToolCalled
{
    public Guid ExecutionId { get; init; }
    public Guid StepId { get; init; }
    public Guid StepResultId { get; init; }
    public Guid? ProjectId { get; init; }
    public Guid? WorkflowDefinitionId { get; init; }
    public int? StepOrder { get; init; }
    public string? StepLabel { get; init; }
    public string? AgentType { get; init; }
    public string? AgentName { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public int Sequence { get; init; }
    public string ToolName { get; init; } = string.Empty;
    public string? ArgumentsPreview { get; init; }
    public string? ResultPreview { get; init; }
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Published when model reasoning text is available for a workflow step.
/// </summary>
public record WorkflowStepReasoningCaptured
{
    public Guid ExecutionId { get; init; }
    public Guid StepId { get; init; }
    public Guid StepResultId { get; init; }
    public Guid? ProjectId { get; init; }
    public Guid? WorkflowDefinitionId { get; init; }
    public int? StepOrder { get; init; }
    public string? StepLabel { get; init; }
    public string? AgentType { get; init; }
    public string? AgentName { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public int Sequence { get; init; }
    public string Content { get; init; } = string.Empty;
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Published once per completed turn in a <c>StepType.EditRoom</c> group-chat run (a seat's or the
/// director's turn). Structural twin of <see cref="WorkflowStepReasoningCaptured"/>: append-only
/// and sequence-numbered (<see cref="TurnIndex"/>), NOT a reuse of <see cref="WorkflowStepProgress"/>
/// — that event's "a later progress event supersedes any earlier one" semantics are wrong for a
/// transcript where every turn matters. Relayed by the Go API as an SSE event (a separate,
/// follow-up pass — not implemented by this event's addition) and rendered as a running transcript
/// card, mirroring how <see cref="WorkflowStepReasoningCaptured"/> becomes a "step.reasoning" card
/// today. <see cref="Text"/> is free-form model prose and is NEVER authoritative for the actual cut
/// decision — see docs/video-editing.md "The edit room" and <c>EditRoomStepExecutor</c>'s own
/// transcript-artifact doc comment for the same caveat.
/// </summary>
public record WorkflowStepChatTurn
{
    public Guid ExecutionId { get; init; }
    public Guid StepId { get; init; }
    public Guid StepResultId { get; init; }
    public Guid? ProjectId { get; init; }
    public Guid? WorkflowDefinitionId { get; init; }
    public int? StepOrder { get; init; }
    public string? StepLabel { get; init; }
    public string CorrelationId { get; init; } = string.Empty;

    /// <summary>0-based, append-only turn index within the room.</summary>
    public int TurnIndex { get; init; }

    /// <summary>The configured turn ceiling (<c>EditRoomStepConfig.MaxTurns</c>), for a "turn 3 of 8" display. Null when not known.</summary>
    public int? TotalTurns { get; init; }

    /// <summary>The seat/persona name that spoke, e.g. "PacingEditor" or "Director".</summary>
    public string Speaker { get; init; } = string.Empty;

    /// <summary>"editor" or "director".</summary>
    public string SpeakerRole { get; init; } = string.Empty;

    /// <summary>The turn's text, truncated to roughly 600 characters at a word boundary. Free-form model prose — never authoritative, see the type's doc comment.</summary>
    public string Text { get; init; } = string.Empty;

    public bool Truncated { get; init; }

    /// <summary>
    /// Offered shot/silence/segment ids this turn mentioned, server-extracted with the same regex
    /// <c>EditRoomGroupChatManager</c> uses for convergence checking, filtered against the id set
    /// actually offered to the room — display/audit only, never trusted as the decision itself.
    /// </summary>
    public IReadOnlyList<string> IdsMentioned { get; init; } = [];

    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Published when a step starts execution.
/// </summary>
public record WorkflowStepStarted
{
    public Guid ExecutionId { get; init; }
    public Guid StepId { get; init; }
    public Guid StepResultId { get; init; }
    public Guid? ProjectId { get; init; }
    public Guid? WorkflowDefinitionId { get; init; }
    public int? StepOrder { get; init; }
    public string? StepLabel { get; init; }
    public string? StepType { get; init; }
    public int? IterationNumber { get; init; }
    public string? AgentType { get; init; }
    public string? AgentName { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public string? InputPreview { get; init; }
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Published on unrecoverable failure.
/// </summary>
public record WorkflowExecutionFailed
{
    public Guid ExecutionId { get; init; }
    public Guid? ProjectId { get; init; }
    public Guid? WorkflowDefinitionId { get; init; }
    public Guid? InitiatedByUserId { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
    public DateTime FailedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Published by Inference API when a project file requires vector index update.
/// </summary>
public record ProjectFileIndexingRequested
{
    public Guid ProjectId { get; init; }
    public Guid FileId { get; init; }
    public string Operation { get; init; } = string.Empty; // Upsert | Delete
    public DateTime RequestedAt { get; init; } = DateTime.UtcNow;
}
