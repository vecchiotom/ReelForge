using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Agents.AI;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Data.OutputSchemas;
using ReelForge.Shared.Inference;
using ReelForge.Shared.Workflows;
using ReelForge.WorkflowEngine.Agents;
using ReelForge.WorkflowEngine.Agents.Rooms;
using ReelForge.WorkflowEngine.Agents.Tools;
using ReelForge.WorkflowEngine.Services.Storage;
using ReelForge.WorkflowEngine.Services.Video;

namespace ReelForge.WorkflowEngine.Execution.StepExecutors;

/// <summary>
/// Executes <see cref="StepType.ColorGradeRoom"/> steps: several
/// <see cref="AgentType.Colorist"/>-role colorist seats plus an
/// <see cref="AgentType.ColorGradeDirector"/> moderator converse in a live group chat over the
/// same bounded <c>VideoAnalyze</c> view (its <c>view.shots</c> ids and measured per-shot colour
/// descriptors) a solo colorist would see, then the director emits ONE schema-validated
/// <see cref="ColorGradePlanOutput"/> in a normal structured call OUTSIDE the chat loop.
/// <see cref="VideoCompileStepExecutor"/>'s <c>ColorGradePlan</c> resolution consumes it exactly
/// as it would a solo <see cref="AgentType.Colorist"/> Agent step's output — the additive
/// <c>"room"</c> metadata key is skipped by deserialization, the same contract the edit and
/// graphics rooms established. See docs/video-editing.md "The color grade room".
///
/// <para>
/// All room mechanics live in the shared <see cref="RoomStepExecutorBase{TDecision}"/> — this
/// class binds only the grade room's domain: its config column, its <c>s{n}</c> shot-id
/// deliberation vocabulary, its charter/director prompts, its seat/director/solo agent types,
/// and its distinctive validation shape. Distinctive vs. both prior rooms: the DECISION carries
/// no ids at all (one whole-program grade in enum words), so <see cref="FilterToOfferedIds"/> is
/// a structural no-op — the shot ids exist purely to anchor the deliberation and its convergence
/// detection — and the words themselves are validated instead: an empty <c>Look</c> is rejected
/// at synthesis (retry), an unrecognized one spends one retry with precise guidance and is
/// otherwise left for <see cref="VideoCompileStepExecutor"/>'s own <c>unknown_look_word</c>
/// degrade, and a <c>Look</c> of <c>"None"</c> is a fully valid outcome (like the graphics
/// room's empty plan, unlike the edit room's never-empty Keep list). No tool restriction
/// override is needed either: the backing agent types are minimal read-only by
/// <c>ToolGroupCatalog</c> in both roles, like the edit room and unlike the graphics room.
/// </para>
/// </summary>
public class ColorGradeRoomStepExecutor : RoomStepExecutorBase<ColorGradePlanOutput>
{
    // Shared, identical instructions for EVERY room participant (seats AND the director's
    // room-participant instance) — same structure and discipline as the edit/graphics rooms'
    // charters, with the grade room's own vocabulary and invariants (never a numeric colour
    // value; the treatment is words; "None" is a valid outcome).
    private const string ColorGradeRoomCharterPrompt =
        """
        You are participating in a live "color grade room" discussion between several colorists
        and a supervising colorist, deciding what ONE colour-grade treatment the whole compiled
        edit of an analyzed video should receive. The bounded view — including a "shots" list
        where each shot has a short opaque id such as "s0" or "s2" and, when available, measured
        visual descriptors in WORDS (colour temperature, tone, saturation, exposure, look
        groups) — was given to you as the first message in this conversation. Every participant
        in this room sees the exact same view.

        ## Rules — hard constraints, not suggestions

        - Ground every argument in shot ids that appear in the "shots" list you were given
          ("s0", "s2", ...) and in the view's MEASURED descriptor words. Never invent a shot id
          or a measurement, and never treat a silence/segment/placement id ("g3", "t7", "p1")
          as a shot — those are a different kind of id.
        - NEVER mention, estimate, or output an RGB value, a hex colour, a curve or level
          number, a gamma/gain/lift/contrast/saturation value, a percentage, or a timestamp, in
          this discussion. The startSec/endSec values on each shot are for READING only — to
          tell shots apart — never echo, adjust, or derive a number from them. A separate
          deterministic step resolves the room's word choices to exact filter parameters.
        - The treatment is WORDS, not numbers: a Look of "None", "Warm", "Cool", "Filmic",
          "Vibrant", "Muted", or "Mono"; a Strength of "Subtle", "Normal", or "Strong"; a
          ShadowTone of "Neutral", "Lifted", or "Deepened"; a HighlightTone of "Neutral",
          "Softened", or "Brightened". Discuss them only as words.
        - The decision is ONE whole-program treatment that must suit every kept shot, not just
          the best one. Prefer restraint: "Subtle"/"Normal" over "Strong", and a Look of "None"
          is a perfectly good outcome for footage that is already well exposed and consistent.
        - Keep every turn SHORT — a few sentences of prose, not an essay. This is a live
          discussion, not a final report.
        - Do NOT output JSON, markdown code fences, or any structured format in this
          discussion — plain conversational prose only. A separate call, made after this
          discussion ends, produces the final structured plan.
        """;

    public ColorGradeRoomStepExecutor(
        IAgentChatClientProvider chatClients,
        IAgentToolProvider toolProvider,
        IAgentRegistry agentRegistry,
        IProjectFileWorkspace workspace,
        IWorkflowExecutionContextAccessor executionContextAccessor,
        ILogger<ColorGradeRoomStepExecutor> logger)
        : base(chatClients, toolProvider, agentRegistry, workspace, executionContextAccessor, logger)
    {
    }

    public override StepType StepType => StepType.ColorGradeRoom;

    protected override string RoomKind => "ColorGradeRoom";

    protected override string RoomDisplayName => "Color grade room";

    protected override string FailureCodePrefix => "COLOR_GRADE_ROOM";

    protected override string ConfigPropertyName => "ColorGradeRoomConfigJson";

    protected override string? GetConfigJson(WorkflowStep step) => step.ColorGradeRoomConfigJson;

    protected override IRoomStepConfig? DeserializeConfig(string json) =>
        JsonSerializer.Deserialize<ColorGradeRoomStepConfig>(json, ConfigJsonOptions);

    protected override HashSet<string> ExtractOfferedIds(JsonNode? root)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (root?["view"]?["shots"] is not JsonArray shots)
            return ids;

        foreach (JsonNode? item in shots)
        {
            string? id = item?["id"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(id))
                ids.Add(id);
        }

        return ids;
    }

    protected override IReadOnlyList<string> ExtractIdMentions(string? text, IReadOnlySet<string> offeredIds) =>
        ColorGradeRoomGroupChatManager.ExtractOfferedIdMentions(text, offeredIds);

    protected override string NoOfferedIdsMessage =>
        "The resolved view offers no shots to deliberate a colour grade over.";

    /// <summary>
    /// A view with zero shots offers nothing to grade — completing with an explicit "None" plan
    /// (rather than failing) keeps a grade-room workflow runnable on such input, the same
    /// graceful outcome the graphics room's empty plan models. "None" is itself the schema's
    /// first-class no-grade word, so downstream <c>VideoCompileStepExecutor</c> consumption
    /// needs no special case.
    /// </summary>
    protected override ColorGradePlanOutput BuildDecisionForEmptyView() => new()
    {
        Look = "None",
        Strength = "Normal",
        ShadowTone = "Neutral",
        HighlightTone = "Neutral",
        PlanRationale = "No shots were offered by the analysis view, so no colour grade was planned."
    };

    protected override string RoomCharterPrompt => ColorGradeRoomCharterPrompt;

    protected override AgentType SeatAgentType => AgentType.Colorist;

    protected override AgentType DirectorAgentType => AgentType.ColorGradeDirector;

    protected override AgentType SoloFallbackAgentType => AgentType.Colorist;

    protected override string SeatRoleNoun => "colorist";

    protected override string DirectorTurnDirective =>
        "You are now speaking as Director, the supervising colorist moderating this color grade " +
        "room. Review the discussion so far. If the room has converged on one coherent treatment " +
        "— including possibly agreeing that NO grade (a Look of \"None\") is warranted — end " +
        "this turn with the literal token ROOM_DECIDED followed by a one- or two-sentence " +
        "summary of what was agreed. Otherwise, give brief guidance to help the colorists " +
        "converge. Keep this turn short.";

    protected override string SynthesisSchemaInstruction =>
        "output ONLY valid JSON matching the ColorGradePlanOutput schema (a look of \"None\" is " +
        "valid when the room agreed the footage needs no grade).";

    protected override string BuildTurnProgressLabel(bool isDirector, string seatName, int turnNumber, int maxTurns) =>
        isDirector
            ? "Director is reviewing"
            : $"{seatName} is proposing a grade (turn {turnNumber}/{maxTurns})";

    /// <summary>
    /// Null-coalesces every word property (explicit JSON nulls would otherwise leave CLR nulls
    /// behind the non-null string defaults) — the same hardening the graphics room applies to its
    /// Overlays list. Word VALIDATION deliberately does not happen here: unknown words are a
    /// retry concern (<see cref="CheckRetryableIssue"/>) and, terminally,
    /// <see cref="VideoCompileStepExecutor"/>'s own <c>unknown_look_word</c>/normalize degrade.
    /// </summary>
    protected override void NormalizeDecision(ColorGradePlanOutput decision)
    {
        decision.Look ??= string.Empty;
        decision.Strength ??= string.Empty;
        decision.ShadowTone ??= string.Empty;
        decision.HighlightTone ??= string.Empty;
        decision.Reason ??= string.Empty;
        decision.PlanRationale ??= string.Empty;
    }

    /// <summary>
    /// An empty Look is a hard synthesis defect (the schema's one required decision word —
    /// "no grade" must be said as the word "None", never as silence), retried with guidance.
    /// Everything else, "None" included, is accepted.
    /// </summary>
    protected override string? RejectFreshSynthesis(ColorGradePlanOutput decision) =>
        string.IsNullOrWhiteSpace(decision.Look)
            ? "The plan's `look` was empty. Choose exactly one of: " +
              string.Join(", ", ColorGradeFilterBuilder.AllowedLooks.Select(l => $"\"{l}\"")) +
              " — use \"None\" to decline the grade."
            : null;

    /// <summary>
    /// A non-empty but unrecognized Look word spends one retry with precise guidance; on the
    /// final attempt it is accepted as-is and left to <see cref="VideoCompileStepExecutor"/>'s
    /// own deterministic <c>unknown_look_word</c> degrade — the base template's documented
    /// soft-reject contract.
    /// </summary>
    protected override string? CheckRetryableIssue(ColorGradePlanOutput decision, JsonNode? viewRoot) =>
        !string.IsNullOrWhiteSpace(decision.Look) && !ColorGradeFilterBuilder.AllowedLooks.Contains(decision.Look)
            ? $"The plan's `look` (\"{decision.Look}\") is not a recognized look word. Choose exactly one of: " +
              string.Join(", ", ColorGradeFilterBuilder.AllowedLooks.Select(l => $"\"{l}\"")) + "."
            : null;

    /// <summary>
    /// Structural no-op: a <see cref="ColorGradePlanOutput"/> carries no ids at all (guarded by
    /// <c>ColorGradePlanOutputInvariantTests</c>' shape pin), so there is nothing to filter —
    /// the offered shot ids anchor only the DELIBERATION and its convergence detection, never
    /// the decision.
    /// </summary>
    protected override RoomFilterResult FilterToOfferedIds(ColorGradePlanOutput decision, HashSet<string> offeredIds, JsonNode? viewRoot) =>
        new(0, null);

    /// <summary>Every filtered decision is acceptable — "None" and unknown-word plans both resolve deterministically downstream.</summary>
    protected override string? RejectFilteredDecision(ColorGradePlanOutput decision, bool solo) => null;

    protected override RoomGroupChatManager CreateManager(
        IReadOnlyList<AIAgent> agents, IRoomStepConfig config, IReadOnlySet<string> offeredIds) =>
        new ColorGradeRoomGroupChatManager(agents, (ColorGradeRoomStepConfig)config, offeredIds, "Director");
}
