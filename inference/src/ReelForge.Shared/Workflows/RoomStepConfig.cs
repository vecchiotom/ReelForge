namespace ReelForge.Shared.Workflows;

/// <summary>
/// The shared configuration surface every multi-agent "room" step type exposes — the knobs the
/// generic room infrastructure (<c>RoomGroupChatManager</c> / <c>RoomStepExecutorBase</c> in the
/// WorkflowEngine) actually reads. A concrete room's config record
/// (<see cref="EditRoomStepConfig"/>, <see cref="GraphicsRoomStepConfig"/>, a future
/// color-grading room's config, ...) implements this on top of its own JSON shape, so the shared
/// scheduler/executor never needs to know which room it is running.
///
/// <para>
/// Naming note: <see cref="EditRoomSeat"/> and <see cref="EditRoomTerminationMode"/> are the
/// room-GENERIC seat record and termination enum despite their names — they were introduced by
/// the first room (the edit room) and are kept under their original names deliberately, because
/// renaming them would break the persisted-config JSON contract's documentation trail and the
/// existing test suite for zero behavioral gain. Every room's config reuses them as-is (the JSON
/// shape — <c>{name, persona, agentDefinitionId}</c> seats, string enum values — is identical
/// across rooms).
/// </para>
/// </summary>
public interface IRoomStepConfig
{
    /// <summary>Which step's bounded <c>{view, meta}</c> envelope every seat and the director see. Null resolves to <c>Previous</c>.</summary>
    ExtractInputRef? View { get; }

    /// <summary>The effective seat list: the configured seats when non-empty, otherwise the room's built-in defaults.</summary>
    IReadOnlyList<EditRoomSeat> EffectiveSeats { get; }

    /// <summary>How many full round-robin passes over every seat happen before the director speaks.</summary>
    int Rounds { get; }

    /// <summary>The hard turn ceiling, clamped to the documented 2..20 range.</summary>
    int ClampedMaxTurns { get; }

    /// <summary>Per-agent-definition override for the director seat; null resolves to the room's built-in director agent.</summary>
    Guid? DirectorAgentDefinitionId { get; }

    /// <summary>How the room can end early, on top of the hard <see cref="ClampedMaxTurns"/> ceiling.</summary>
    EditRoomTerminationMode Termination { get; }

    /// <summary>Consecutive rounds with an unchanged offered-id-mention set required for convergence to fire.</summary>
    int MinConvergenceRounds { get; }

    /// <summary>Per-turn output token cap injected into every seat's/the room-participant director's turn.</summary>
    int MaxTurnTokens { get; }

    /// <summary>Soft cap on how much of the room transcript is rendered into the synthesis prompt (oldest turns dropped first).</summary>
    int MaxHistoryChars { get; }

    /// <summary>Sampling temperature injected into every seat's turn.</summary>
    float Temperature { get; }

    /// <summary>Sampling temperature injected into the director's ROOM-PARTICIPANT turns only.</summary>
    float DirectorTemperature { get; }

    /// <summary>Reasoning-effort value injected into every room-participant turn.</summary>
    string ReasoningEffort { get; }

    /// <summary>Hard wall-clock budget for the whole group-chat run (not the later synthesis call).</summary>
    int RoomTimeoutSeconds { get; }

    /// <summary>Whether the full room transcript is uploaded as this step's <c>ArtifactStorageKey</c>. Never authoritative.</summary>
    bool PersistTranscript { get; }

    /// <summary>Whether a <c>WorkflowStepChatTurn</c> integration event is published per completed turn.</summary>
    bool StreamTurns { get; }

    /// <summary>
    /// Whether a failed/empty room decision falls back to ONE ordinary solo call of the room's
    /// underlying single-agent equivalent (the existing pre-room pipeline, unchanged). Each
    /// concrete config exposes this under a domain-specific JSON name
    /// (<c>fallbackToSoloEditor</c> / <c>fallbackToSoloPlanner</c>).
    /// </summary>
    bool FallbackToSolo { get; }

    /// <summary>Retry attempts for the standalone structured-output synthesis call.</summary>
    int MaxSynthesisAttempts { get; }
}
