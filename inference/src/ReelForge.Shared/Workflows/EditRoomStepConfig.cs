namespace ReelForge.Shared.Workflows;

/// <summary>
/// One seat in an edit room: a persona-flavored turn directive layered onto the SAME shared
/// instructions every seat receives (see <c>EditRoomStepExecutor</c>'s "sharedRoomCharter"), plus
/// an optional per-seat <see cref="AgentDefinitionId"/> override. When null (the default for all
/// three built-in seats — see <see cref="EditRoomStepConfig.DefaultSeats"/>), the seat resolves to
/// the built-in <c>AgentType.VideoStoryEditor</c> agent's own seeded prompt/tools/provider — no new
/// <c>AgentType</c> enum member exists per seat, since only the persona differs, and personas are
/// injected per-turn by <c>EditRoomSeatAgent</c>, not baked into separate agent definitions.
/// </summary>
public sealed record EditRoomSeat(string Name, string Persona, Guid? AgentDefinitionId = null);

/// <summary>
/// How <c>EditRoomGroupChatManager.ShouldTerminateAsync</c> decides the room is done, on top of the
/// hard <c>MaximumIterationCount</c> ceiling (<see cref="EditRoomStepConfig.MaxTurns"/>), which
/// always applies regardless of this setting.
/// </summary>
public enum EditRoomTerminationMode
{
    /// <summary>Only the director's literal "ROOM_DECIDED" sentinel ends the room early.</summary>
    SentinelOnly,

    /// <summary>Only offered-id-vocabulary convergence across consecutive editor rounds ends the room early.</summary>
    Converged,

    /// <summary>Either the sentinel or convergence ends the room early — the default.</summary>
    SentinelOrConverged,

    /// <summary>Neither check runs; only the <see cref="EditRoomStepConfig.MaxTurns"/> ceiling ends the room.</summary>
    FixedTurns
}

/// <summary>
/// JSON configuration for a <c>StepType.EditRoom</c> workflow step. Deserialized from
/// <c>WorkflowStep.EditRoomConfigJson</c>. Versioned, every field defaulted so old/partial JSON
/// still deserializes — same discipline as <see cref="VideoAnalyzeStepConfig"/>/
/// <see cref="VideoCompileStepConfig"/>. See docs/video-editing.md "The edit room".
/// </summary>
public sealed record EditRoomStepConfig(
    int Version = 1,
    /// <summary>
    /// Which step's bounded <c>{view, meta}</c> envelope every seat and the director are shown —
    /// an <see cref="ExtractInputRef"/> reused verbatim from <see cref="VideoAnalyzeStepConfig"/>.
    /// Only <c>Previous</c>/<c>Step</c> are meaningful (same restriction
    /// <see cref="VideoCompileStepConfig.Decision"/> already documents).
    /// </summary>
    ExtractInputRef? View = null,
    /// <summary>Editor seats, round-robin scheduled by <c>EditRoomGroupChatManager</c>. Null/empty resolves to <see cref="DefaultSeats"/> at execution time.</summary>
    IReadOnlyList<EditRoomSeat>? Seats = null,
    /// <summary>How many full round-robin passes over every seat happen before the director speaks.</summary>
    int Rounds = 2,
    /// <summary>Hard turn ceiling (maps to <c>GroupChatManager.MaximumIterationCount</c>), clamped 2..20. Deliberately far below the framework's own default of 40 — see docs/video-editing.md.</summary>
    int MaxTurns = 8,
    /// <summary>Per-agent-definition override for the director seat; null resolves to the built-in <c>AgentType.VideoEditDirector</c> agent.</summary>
    Guid? DirectorAgentDefinitionId = null,
    EditRoomTerminationMode Termination = EditRoomTerminationMode.SentinelOrConverged,
    /// <summary>Consecutive rounds with an unchanged offered-id-mention set required before <see cref="EditRoomTerminationMode.Converged"/>/<see cref="EditRoomTerminationMode.SentinelOrConverged"/> ends the room early.</summary>
    int MinConvergenceRounds = 2,
    /// <summary>Per-turn output token cap injected into every seat's/the room-participant director's <c>ChatOptions.MaxOutputTokens</c>.</summary>
    int MaxTurnTokens = 220,
    /// <summary>Soft cap on how much of the room transcript is rendered into the synthesis prompt (oldest turns dropped first beyond this).</summary>
    int MaxHistoryChars = 40000,
    /// <summary>Sampling temperature injected into every editor seat's turn.</summary>
    float Temperature = 0.7f,
    /// <summary>Sampling temperature injected into the director's ROOM-PARTICIPANT turns only — the standalone synthesis call uses <c>VideoEditDirectorAgent</c>'s own <c>AgentModelSettings</c> default instead.</summary>
    float DirectorTemperature = 0.3f,
    /// <summary>
    /// Reasoning-effort value injected into every seat's (and the room-participant director's)
    /// turn via the same <c>RawRepresentationFactory</c> mechanism <c>ReelForgeAgentBase</c> uses —
    /// must pass <c>ReelForgeAgentBase.ValidReasoningEfforts</c>.
    /// </summary>
    string ReasoningEffort = "none",
    /// <summary>Hard wall-clock budget for the whole group-chat run (not the later synthesis call).</summary>
    int RoomTimeoutSeconds = 1200,
    /// <summary>Whether the full, unabridged room transcript is uploaded as this step's <c>ArtifactStorageKey</c>. Never authoritative — see docs/video-editing.md.</summary>
    bool PersistTranscript = true,
    /// <summary>Whether a <c>WorkflowStepChatTurn</c> integration event is published per completed turn (progress reporting via <c>context.ReportProgressAsync</c> always happens regardless of this flag).</summary>
    bool StreamTurns = true,
    /// <summary>Whether a failed/empty room decision falls back to one ordinary solo <c>AgentType.VideoStoryEditor</c> call — today's existing single-editor pipeline, unchanged.</summary>
    bool FallbackToSoloEditor = true,
    /// <summary>Retry attempts for the standalone structured-output synthesis call when the result is empty/unparseable/all-ids-dropped.</summary>
    int MaxSynthesisAttempts = 2)
{
    /// <summary>
    /// The three built-in seats used whenever <see cref="Seats"/> is null/empty — validated live to
    /// be the sweet spot (more seats didn't add value, fewer produced repetitive agreement). All
    /// three resolve to the same built-in <c>AgentType.VideoStoryEditor</c> agent; only the persona
    /// differs.
    /// </summary>
    public static readonly IReadOnlyList<EditRoomSeat> DefaultSeats =
    [
        new("PacingEditor", "argues for rhythm — dead air, false starts, still-boundaries, shot length"),
        new("StoryEditor", "argues for narrative — complete thoughts, transcript arc, whether the kept material answers the question the opening poses"),
        new("CraftEditor", "argues for material quality — duplicate/best takes, look-group cohesion, caption issues, exposure")
    ];

    /// <summary>The effective seat list: <see cref="Seats"/> when non-empty, otherwise <see cref="DefaultSeats"/>.</summary>
    public IReadOnlyList<EditRoomSeat> EffectiveSeats => Seats is { Count: > 0 } ? Seats : DefaultSeats;

    /// <summary><see cref="MaxTurns"/> clamped to the documented 2..20 range.</summary>
    public int ClampedMaxTurns => Math.Clamp(MaxTurns, 2, 20);
}
