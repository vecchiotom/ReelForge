namespace ReelForge.Shared.Workflows;

/// <summary>
/// JSON configuration for a <c>StepType.ColorGradeRoom</c> workflow step — the colour-grading
/// analogue of <see cref="EditRoomStepConfig"/>/<see cref="GraphicsRoomStepConfig"/>, built on the
/// same shared room infrastructure (<see cref="IRoomStepConfig"/>,
/// <c>RoomGroupChatManager</c>/<c>RoomStepExecutorBase</c> in the WorkflowEngine). Several
/// colorist seats plus a supervising-colorist director deliberate over the SAME bounded
/// <c>VideoAnalyze</c> view (its measured per-shot colour temperature/tone/saturation words and
/// <c>s{n}</c> shot ids) a solo <c>AgentType.Colorist</c> consumes, then the director synthesizes
/// ONE schema-validated <c>ColorGradePlanOutput</c> — the exact schema the solo colorist already
/// produces, so <c>VideoCompileStepExecutor</c>'s <c>ColorGradePlan</c> resolution consumes both
/// identically. Deserialized from <c>WorkflowStep.ColorGradeRoomConfigJson</c>. Versioned, every
/// field defaulted so old/partial JSON still deserializes — same discipline as
/// <see cref="EditRoomStepConfig"/>. See docs/video-editing.md "The color grade room".
///
/// <para>
/// Seats reuse the room-generic <see cref="EditRoomSeat"/> record and
/// <see cref="EditRoomTerminationMode"/> enum — see <see cref="IRoomStepConfig"/>'s naming note.
/// </para>
/// </summary>
public sealed record ColorGradeRoomStepConfig(
    int Version = 1,
    /// <summary>
    /// Which step's bounded <c>{view, meta}</c> envelope every seat and the director are shown.
    /// This should point at the <c>VideoAnalyze</c> step whose view carries the measured per-shot
    /// visual descriptors the colorists argue from — usually an explicit <c>Step</c> reference,
    /// since <c>Previous</c> relative to a grade-room step typically resolves to the story
    /// editor's decision, not the analyze envelope. Only <c>Previous</c>/<c>Step</c> are
    /// meaningful.
    /// </summary>
    ExtractInputRef? View = null,
    /// <summary>Colorist seats, round-robin scheduled. Null/empty resolves to <see cref="DefaultSeats"/> at execution time.</summary>
    IReadOnlyList<EditRoomSeat>? Seats = null,
    /// <summary>How many full round-robin passes over every seat happen before the director speaks.</summary>
    int Rounds = 2,
    /// <summary>Hard turn ceiling (maps to <c>GroupChatManager.MaximumIterationCount</c>), clamped 2..20.</summary>
    int MaxTurns = 8,
    /// <summary>Per-agent-definition override for the director seat; null resolves to the built-in <c>AgentType.ColorGradeDirector</c> agent.</summary>
    Guid? DirectorAgentDefinitionId = null,
    EditRoomTerminationMode Termination = EditRoomTerminationMode.SentinelOrConverged,
    /// <summary>Consecutive rounds with an unchanged offered-shot-id-mention set required before convergence ends the room early.</summary>
    int MinConvergenceRounds = 2,
    /// <summary>Per-turn output token cap injected into every seat's/the room-participant director's <c>ChatOptions.MaxOutputTokens</c>.</summary>
    int MaxTurnTokens = 220,
    /// <summary>Soft cap on how much of the room transcript is rendered into the synthesis prompt (oldest turns dropped first beyond this).</summary>
    int MaxHistoryChars = 40000,
    /// <summary>Sampling temperature injected into every colorist seat's turn.</summary>
    float Temperature = 0.7f,
    /// <summary>Sampling temperature injected into the director's ROOM-PARTICIPANT turns only — the standalone synthesis call uses <c>ColorGradeDirectorAgent</c>'s own <c>AgentModelSettings</c> default instead.</summary>
    float DirectorTemperature = 0.3f,
    /// <summary>Reasoning-effort value injected into every room-participant turn — must pass <c>ReelForgeAgentBase.ValidReasoningEfforts</c>.</summary>
    string ReasoningEffort = "none",
    /// <summary>Hard wall-clock budget for the whole group-chat run (not the later synthesis call).</summary>
    int RoomTimeoutSeconds = 1200,
    /// <summary>Whether the full, unabridged room transcript is uploaded as this step's <c>ArtifactStorageKey</c>. Never authoritative — see docs/video-editing.md.</summary>
    bool PersistTranscript = true,
    /// <summary>Whether a <c>WorkflowStepChatTurn</c> integration event is published per completed turn (progress reporting always happens regardless).</summary>
    bool StreamTurns = true,
    /// <summary>Whether a failed room falls back to one ordinary solo <c>AgentType.Colorist</c> call — the single-colorist pipeline, unchanged.</summary>
    bool FallbackToSoloColorist = true,
    /// <summary>Retry attempts for the standalone structured-output synthesis call when the result is unparseable.</summary>
    int MaxSynthesisAttempts = 2) : IRoomStepConfig
{
    /// <summary>
    /// The three built-in seats used whenever <see cref="Seats"/> is null/empty, dividing the
    /// actual grading decision space (exposure/contrast, colour cast/saturation, restraint and
    /// cross-shot consistency) rather than forcing arbitrary personas. All three resolve to the
    /// same built-in <c>AgentType.Colorist</c> agent; only the persona differs — the same
    /// one-agent-many-personas pattern <see cref="EditRoomStepConfig.DefaultSeats"/> established.
    /// </summary>
    public static readonly IReadOnlyList<EditRoomSeat> DefaultSeats =
    [
        new("ToneArtist", "argues for exposure and contrast — which shots read crushed, washed out, or backlit, whether shadows want lifting or deepening, keeping faces readable"),
        new("PaletteArtist", "argues for colour — the measured temperature and saturation words per shot, which cast the footage wants leaned into or corrected away, which named look suits it"),
        new("ContinuityArtist", "argues for restraint and consistency — one grade must suit every kept shot, look-group cohesion, when Subtle beats Strong, and when None is the right call")
    ];

    /// <summary>The effective seat list: <see cref="Seats"/> when non-empty, otherwise <see cref="DefaultSeats"/>.</summary>
    public IReadOnlyList<EditRoomSeat> EffectiveSeats => Seats is { Count: > 0 } ? Seats : DefaultSeats;

    /// <summary><see cref="MaxTurns"/> clamped to the documented 2..20 range.</summary>
    public int ClampedMaxTurns => Math.Clamp(MaxTurns, 2, 20);

    /// <summary><see cref="IRoomStepConfig"/>'s room-generic name for <see cref="FallbackToSoloColorist"/> (JSON property stays <c>fallbackToSoloColorist</c>).</summary>
    bool IRoomStepConfig.FallbackToSolo => FallbackToSoloColorist;
}
