using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ReelForge.Shared.Agents;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Data.OutputSchemas;
using ReelForge.Shared.Inference;
using ReelForge.Shared.Workflows;
using ReelForge.WorkflowEngine.Agents;
using ReelForge.WorkflowEngine.Agents.Rooms;
using ReelForge.WorkflowEngine.Agents.Tools;
using ReelForge.WorkflowEngine.Services.Storage;

namespace ReelForge.WorkflowEngine.Execution.StepExecutors;

/// <summary>
/// Executes <see cref="StepType.GraphicsRoom"/> steps: several
/// <see cref="AgentType.MotionGraphicsPlanner"/>-role artist seats plus an
/// <see cref="AgentType.MotionGraphicsDirector"/> moderator converse in a live group chat over the
/// same offered <c>view.placements</c> candidates (<c>p{n}</c> ids) a solo planner would see, then
/// the director emits ONE schema-validated <see cref="MotionGraphicsPlanOutput"/> in a normal
/// structured call OUTSIDE the chat loop. <see cref="VideoCompileStepExecutor"/>'s
/// <c>GraphicsPlan</c> resolution needs ZERO changes — it already consumes
/// <see cref="MotionGraphicsPlanOutput"/> from whichever step the compile config points at, and
/// its deserialization skips the additive <c>"room"</c> metadata key exactly as it does for the
/// edit room's decision. See docs/video-editing.md "The graphics room".
///
/// <para>
/// All room mechanics live in the shared <see cref="RoomStepExecutorBase{TDecision}"/> — this
/// class binds only the graphics room's domain: its config column, its <c>p{n}</c> placement-id
/// vocabulary, its charter/director prompts, its seat/director/solo agent types, its "an empty
/// overlay plan is a VALID outcome" validation (unlike the edit room, where an empty Keep list is
/// never acceptable), and two graphics-specific behaviors: room-participant turns are
/// tool-restricted to the read-only subset of the backing agents' grants (the seats' underlying
/// <see cref="AgentType.MotionGraphicsPlanner"/> carries sandbox+render tools that belong in the
/// standalone synthesis call, never in a ~220-token prose turn), and the bounded view is enriched
/// with the same <c>inEdit</c> survival annotation
/// (<see cref="IMotionGraphicsPlacementAnnotator"/>) a solo planner's prompt gets.
/// </para>
/// </summary>
public class GraphicsRoomStepExecutor : RoomStepExecutorBase<MotionGraphicsPlanOutput>
{
    // Shared, identical instructions for EVERY room participant (seats AND the director's
    // room-participant instance) — same structure and discipline as the edit room's charter, with
    // the graphics room's own id namespace and invariants (never a timestamp OR a coordinate;
    // duration/emphasis are words; zero overlays is a valid outcome).
    private const string GraphicsRoomCharterPrompt =
        """
        You are participating in a live "graphics room" discussion between several motion-graphics
        artists and a lead director, deciding which overlay-placement candidates of an analyzed
        video deserve a motion-graphics overlay (a lower-third, title, callout, or tag) and what
        each overlay should say. The bounded view — including a "placements" list of candidates,
        each with a short opaque id such as "p0" or "p3", its named region, a 0-100 "fit" score, a
        light/dark text hint, and (when available) an "inEdit" flag saying whether that candidate's
        moment survives the story editor's cut — was given to you as the first message in this
        conversation. Every participant in this room sees the exact same view.

        ## Rules — hard constraints, not suggestions

        - Reference ONLY placement ids that appear in the "placements" list you were given ("p0",
          "p3", ...). Never invent one, never guess one, and never treat a shot/silence/segment id
          ("s2", "g3", "t7") as a placement — those are a different kind of id and are never valid
          as an overlay anchor here.
        - NEVER mention, estimate, or output a timestamp, a duration in seconds or frames, or any
          pixel/percentage coordinate, in this discussion. The startSec/endSec values on each
          placement are for READING only — to tell otherwise-identical candidates apart — never
          echo, adjust, or derive a number from them. A separate deterministic step resolves chosen
          placement ids to exact positions and times.
        - Overlay duration and emphasis are WORDS, not numbers: "Short"/"Medium"/"Hold" and
          "Subtle"/"Normal"/"Strong". Discuss them only as words.
        - Strongly prefer placements marked "inEdit": true — an overlay planned on a cut-away
          moment is dropped later and wasted. Prefer fewer, better overlays over decoration on
          every cut; ZERO overlays is a perfectly good outcome for footage that needs none, and
          never plan two overlapping overlays in the same region.
        - Keep every turn SHORT — a few sentences of prose, not an essay. This is a live
          discussion, not a final report.
        - Do NOT output JSON, markdown code fences, or any structured format in this discussion —
          plain conversational prose only. A separate call, made after this discussion ends,
          produces the final structured plan (and is the only place a rendered graphic asset may
          be produced — do not attempt to use sandbox or render tools during this discussion).
        """;

    // The read-only room-turn tool allowlist: ProjectRead + WorkflowControl function names, taken
    // from the same ToolGroupCatalog both AgentToolProvider and the seeder's display metadata
    // derive from, so this subset can never drift from the catalog's definition of "read-only".
    private static readonly HashSet<string> RoomTurnToolAllowlist = new(
        ToolGroupCatalog.FunctionNamesFor(ToolGroup.ProjectRead)
            .Concat(ToolGroupCatalog.FunctionNamesFor(ToolGroup.WorkflowControl)),
        StringComparer.Ordinal);

    private readonly IMotionGraphicsPlacementAnnotator _placementAnnotator;

    public GraphicsRoomStepExecutor(
        IAgentChatClientProvider chatClients,
        IAgentToolProvider toolProvider,
        IAgentRegistry agentRegistry,
        IProjectFileWorkspace workspace,
        IWorkflowExecutionContextAccessor executionContextAccessor,
        IMotionGraphicsPlacementAnnotator placementAnnotator,
        ILogger<GraphicsRoomStepExecutor> logger)
        : base(chatClients, toolProvider, agentRegistry, workspace, executionContextAccessor, logger)
    {
        _placementAnnotator = placementAnnotator;
    }

    public override StepType StepType => StepType.GraphicsRoom;

    protected override string RoomKind => "GraphicsRoom";

    protected override string RoomDisplayName => "Graphics room";

    protected override string FailureCodePrefix => "GRAPHICS_ROOM";

    protected override string ConfigPropertyName => "GraphicsRoomConfigJson";

    protected override string? GetConfigJson(WorkflowStep step) => step.GraphicsRoomConfigJson;

    protected override IRoomStepConfig? DeserializeConfig(string json) =>
        JsonSerializer.Deserialize<GraphicsRoomStepConfig>(json, ConfigJsonOptions);

    protected override HashSet<string> ExtractOfferedIds(JsonNode? root)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (root?["view"]?["placements"] is not JsonArray placements)
            return ids;

        foreach (JsonNode? item in placements)
        {
            string? id = item?["id"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(id))
                ids.Add(id);
        }

        return ids;
    }

    protected override IReadOnlyList<string> ExtractIdMentions(string? text, IReadOnlySet<string> offeredIds) =>
        GraphicsRoomGroupChatManager.ExtractOfferedIdMentions(text, offeredIds);

    protected override string NoOfferedIdsMessage =>
        "The resolved view offers no overlay-placement ids to decide over.";

    /// <summary>
    /// Unlike the edit room (where a view with nothing to keep is a genuine upstream error), a
    /// video whose analysis offered zero overlay-placement candidates legitimately warrants zero
    /// overlays — the same graceful outcome the solo planner's "output an empty overlays list"
    /// rule and <c>VideoCompileStepExecutor</c>'s no-graphics degrade already model. Completing
    /// with an empty plan (rather than failing) keeps a graphics-room workflow runnable on such
    /// footage.
    /// </summary>
    protected override MotionGraphicsPlanOutput BuildDecisionForEmptyView() => new()
    {
        Overlays = [],
        PlanRationale = "No overlay-placement candidates were offered by the analysis view, so no overlays were planned."
    };

    protected override string RoomCharterPrompt => GraphicsRoomCharterPrompt;

    protected override AgentType SeatAgentType => AgentType.MotionGraphicsPlanner;

    protected override AgentType DirectorAgentType => AgentType.MotionGraphicsDirector;

    protected override AgentType SoloFallbackAgentType => AgentType.MotionGraphicsPlanner;

    protected override string SeatRoleNoun => "motion-graphics artist";

    protected override string DirectorTurnDirective =>
        "You are now speaking as Director, the lead motion-graphics artist moderating this " +
        "graphics room. Review the discussion so far. If the room has converged on a coherent " +
        "overlay plan — including possibly agreeing that NO overlays are warranted — end this " +
        "turn with the literal token ROOM_DECIDED followed by a one- or two-sentence summary of " +
        "what was agreed. Otherwise, give brief guidance to help the artists converge. Keep this " +
        "turn short.";

    protected override string SynthesisSchemaInstruction =>
        "output ONLY valid JSON matching the MotionGraphicsPlanOutput schema (an empty overlays " +
        "list is valid when the room agreed none are warranted).";

    protected override string BuildTurnProgressLabel(bool isDirector, string seatName, int turnNumber, int maxTurns) =>
        isDirector
            ? "Director is reviewing"
            : $"{seatName} is proposing overlays (turn {turnNumber}/{maxTurns})";

    protected override void NormalizeDecision(MotionGraphicsPlanOutput decision) =>
        decision.Overlays ??= [];

    /// <summary>An empty overlay plan is a valid synthesis outcome — never retried.</summary>
    protected override string? RejectFreshSynthesis(MotionGraphicsPlanOutput decision) => null;

    // -----------------------------------------------------------------
    // Deterministic validation — never trust the model: any overlay whose PlacementId isn't in the
    // offered p{n} set is dropped (recorded in the output's room.droppedSpanCount), the identical
    // discipline VideoCompileStepExecutor applies again at compile time (unknown_placement).
    // -----------------------------------------------------------------

    protected override int FilterToOfferedIds(MotionGraphicsPlanOutput decision, HashSet<string> offeredIds)
    {
        decision.Overlays ??= [];
        int before = decision.Overlays.Count;
        decision.Overlays = decision.Overlays
            .Where(o => !string.IsNullOrEmpty(o.PlacementId) && offeredIds.Contains(o.PlacementId))
            .ToList();
        return before - decision.Overlays.Count;
    }

    /// <summary>A filtered-to-empty plan is still valid — zero overlays never degrades the room.</summary>
    protected override string? RejectFilteredDecision(MotionGraphicsPlanOutput decision, bool solo) => null;

    protected override RoomGroupChatManager CreateManager(
        IReadOnlyList<AIAgent> agents, IRoomStepConfig config, IReadOnlySet<string> offeredIds) =>
        new GraphicsRoomGroupChatManager(agents, (GraphicsRoomStepConfig)config, offeredIds, "Director");

    /// <summary>
    /// Restricts every in-room agent (seats AND the director's room-participant instance) to the
    /// read-only ProjectRead + WorkflowControl subset of its grant. The backing agent types carry
    /// the full sandbox+Remotion+render tool set (see ToolGroupCatalog's MotionGraphicsDirector
    /// rationale) so the standalone synthesis call can render a real overlay asset — but a room
    /// turn is a short free-form prose contribution capped at ~220 output tokens, where a sandbox
    /// tool call is at best wasted work and at worst a place for view-derived text to reach code
    /// execution with no structured-output validation in between.
    /// </summary>
    protected override IReadOnlyList<AIFunction> GetRoomTurnTools(AgentType agentType) =>
        ToolProvider.GetTools(agentType)
            .Where(t => RoomTurnToolAllowlist.Contains(t.Name))
            .ToList();

    /// <summary>
    /// Enriches the analyze step's <c>view.placements</c> with the same <c>inEdit</c> survival
    /// annotation a solo <see cref="AgentType.MotionGraphicsPlanner"/> Agent step's prompt gets
    /// (see <see cref="IMotionGraphicsPlacementAnnotator"/>) — installed as a prompt-output
    /// override, which the base's view resolution reads through
    /// <see cref="StepExecutionContext.OutputForPrompt"/>. Soft-fails to the plain view by the
    /// annotator's own never-fails contract.
    /// </summary>
    protected override Task PrepareViewAsync(StepExecutionContext context) =>
        _placementAnnotator.AnnotateAsync(context, context.CancellationToken);
}
