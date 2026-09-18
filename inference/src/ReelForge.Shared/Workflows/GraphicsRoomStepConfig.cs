namespace ReelForge.Shared.Workflows;

/// <summary>
/// JSON configuration for a <c>StepType.GraphicsRoom</c> workflow step — the motion-graphics
/// analogue of <see cref="EditRoomStepConfig"/>, built on the same shared room infrastructure
/// (<see cref="IRoomStepConfig"/>, <c>RoomGroupChatManager</c>/<c>RoomStepExecutorBase</c> in the
/// WorkflowEngine). Several motion-graphics-artist seats plus a lead-artist director deliberate
/// over the SAME offered <c>view.placements</c> candidates (<c>p{n}</c> ids) the solo
/// <c>AgentType.MotionGraphicsPlanner</c> consumes, then the director synthesizes ONE
/// schema-validated <c>MotionGraphicsPlanOutput</c> — the exact schema the solo planner already
/// produces, so <c>VideoCompileStepExecutor</c>'s <c>GraphicsPlan</c> resolution needs zero
/// changes to consume it. Deserialized from <c>WorkflowStep.GraphicsRoomConfigJson</c>.
/// Versioned, every field defaulted so old/partial JSON still deserializes — same discipline as
/// <see cref="EditRoomStepConfig"/>. See docs/video-editing.md "The graphics room".
///
/// <para>
/// Seats reuse the room-generic <see cref="EditRoomSeat"/> record and
/// <see cref="EditRoomTerminationMode"/> enum — see <see cref="IRoomStepConfig"/>'s naming note.
/// </para>
/// </summary>
public sealed record GraphicsRoomStepConfig(
    int Version = 1,
    /// <summary>
    /// Which step's bounded <c>{view, meta}</c> envelope every seat and the director are shown.
    /// This must point at the <c>VideoAnalyze</c> step that emitted <c>view.placements</c>
    /// (<c>EmitOverlayPlacements: true</c>) — usually an explicit <c>Step</c> reference, since
    /// <c>Previous</c> relative to a graphics-room step typically resolves to the story editor's
    /// decision, not the analyze envelope. Only <c>Previous</c>/<c>Step</c> are meaningful.
    /// </summary>
    ExtractInputRef? View = null,
    /// <summary>Artist seats, round-robin scheduled. Null/empty resolves to <see cref="DefaultSeats"/> at execution time.</summary>
    IReadOnlyList<EditRoomSeat>? Seats = null,
    /// <summary>How many full round-robin passes over every seat happen before the director speaks.</summary>
    int Rounds = 2,
    /// <summary>Hard turn ceiling (maps to <c>GroupChatManager.MaximumIterationCount</c>), clamped 2..20.</summary>
    int MaxTurns = 8,
    /// <summary>Per-agent-definition override for the director seat; null resolves to the built-in <c>AgentType.MotionGraphicsDirector</c> agent.</summary>
    Guid? DirectorAgentDefinitionId = null,
    EditRoomTerminationMode Termination = EditRoomTerminationMode.SentinelOrConverged,
    /// <summary>Consecutive rounds with an unchanged offered-placement-id-mention set required before convergence ends the room early.</summary>
    int MinConvergenceRounds = 2,
    /// <summary>Per-turn output token cap injected into every seat's/the room-participant director's <c>ChatOptions.MaxOutputTokens</c>.</summary>
    int MaxTurnTokens = 220,
    /// <summary>Soft cap on how much of the room transcript is rendered into the synthesis prompt (oldest turns dropped first beyond this).</summary>
    int MaxHistoryChars = 40000,
    /// <summary>Sampling temperature injected into every artist seat's turn.</summary>
    float Temperature = 0.7f,
    /// <summary>Sampling temperature injected into the director's ROOM-PARTICIPANT turns only — the standalone synthesis call uses <c>MotionGraphicsDirectorAgent</c>'s own <c>AgentModelSettings</c> default instead.</summary>
    float DirectorTemperature = 0.3f,
    /// <summary>Reasoning-effort value injected into every room-participant turn — must pass <c>ReelForgeAgentBase.ValidReasoningEfforts</c>.</summary>
    string ReasoningEffort = "none",
    /// <summary>Hard wall-clock budget for the whole group-chat run (not the later synthesis call).</summary>
    int RoomTimeoutSeconds = 1200,
    /// <summary>Whether the full, unabridged room transcript is uploaded as this step's <c>ArtifactStorageKey</c>. Never authoritative — see docs/video-editing.md.</summary>
    bool PersistTranscript = true,
    /// <summary>Whether a <c>WorkflowStepChatTurn</c> integration event is published per completed turn (progress reporting always happens regardless).</summary>
    bool StreamTurns = true,
    /// <summary>Whether a failed room falls back to one ordinary solo <c>AgentType.MotionGraphicsPlanner</c> call — today's existing single-planner pipeline, unchanged.</summary>
    bool FallbackToSoloPlanner = true,
    /// <summary>Retry attempts for the standalone structured-output synthesis call when the result is unparseable.</summary>
    int MaxSynthesisAttempts = 2) : IRoomStepConfig
{
    /// <summary>
    /// The three built-in seats used whenever <see cref="Seats"/> is null/empty, dividing the
    /// actual overlay decision space (which placement / which moment / what it says and how it's
    /// made) rather than forcing arbitrary personas. All three resolve to the same built-in
    /// <c>AgentType.MotionGraphicsPlanner</c> agent; only the persona differs — the same
    /// one-agent-many-personas pattern <see cref="EditRoomStepConfig.DefaultSeats"/> established.
    /// </summary>
    public static readonly IReadOnlyList<EditRoomSeat> DefaultSeats =
    [
        new("LayoutArtist", "argues for placement and legibility — regions, fit scores, light/dark text hints, clutter, never two overlapping overlays in one region"),
        new("TimingArtist", "argues for timing — which candidate window actually lines up with what is being said or shown, inEdit survival, duration words, not letting an overlay linger stale"),
        new("CopyArtist", "argues for content and craft — what each overlay says, broadcast-lower-third brevity, when a moment deserves a real rendered graphic instead of plain text, and when the right number of overlays is zero")
    ];

    /// <summary>The effective seat list: <see cref="Seats"/> when non-empty, otherwise <see cref="DefaultSeats"/>.</summary>
    public IReadOnlyList<EditRoomSeat> EffectiveSeats => Seats is { Count: > 0 } ? Seats : DefaultSeats;

    /// <summary><see cref="MaxTurns"/> clamped to the documented 2..20 range.</summary>
    public int ClampedMaxTurns => Math.Clamp(MaxTurns, 2, 20);

    /// <summary><see cref="IRoomStepConfig"/>'s room-generic name for <see cref="FallbackToSoloPlanner"/> (JSON property stays <c>fallbackToSoloPlanner</c>).</summary>
    bool IRoomStepConfig.FallbackToSolo => FallbackToSoloPlanner;
}
