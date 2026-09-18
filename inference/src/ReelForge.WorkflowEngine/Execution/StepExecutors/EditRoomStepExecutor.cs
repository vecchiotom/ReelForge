using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Agents.AI;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Data.OutputSchemas;
using ReelForge.Shared.Inference;
using ReelForge.Shared.Workflows;
using ReelForge.WorkflowEngine.Agents;
using ReelForge.WorkflowEngine.Agents.EditRoom;
using ReelForge.WorkflowEngine.Agents.Rooms;
using ReelForge.WorkflowEngine.Agents.Tools;
using ReelForge.WorkflowEngine.Services.Storage;

namespace ReelForge.WorkflowEngine.Execution.StepExecutors;

/// <summary>
/// Executes <see cref="StepType.EditRoom"/> steps: several <see cref="AgentType.VideoStoryEditor"/>-
/// role seats plus an <see cref="AgentType.VideoEditDirector"/> moderator converse in a live
/// <c>Microsoft.Agents.AI.Workflows</c> group chat over a bounded <c>VideoAnalyze</c> view, then the
/// director emits ONE schema-validated <see cref="VideoEditDecisionOutput"/> in a normal structured
/// call OUTSIDE the chat loop. <see cref="VideoCompileStepExecutor"/> needs ZERO
/// changes — it already consumes <see cref="VideoEditDecisionOutput"/> from whichever step a
/// workflow's <c>Decision.StepOrder</c> points at. See docs/video-editing.md "The edit room".
///
/// <para>
/// All room mechanics (scheduling, per-turn options, transcript persistence, synthesis retries,
/// solo fallback, failure envelopes) live in the shared <see cref="RoomStepExecutorBase{TDecision}"/>
/// — this class binds only the edit room's domain: its config column, its <c>s/g/t</c> id
/// vocabulary, its charter/director prompts, its seat/director/solo agent types, and its
/// "an empty Keep list is never acceptable" validation.
/// </para>
/// </summary>
public class EditRoomStepExecutor : RoomStepExecutorBase<VideoEditDecisionOutput>
{
    // Shared, identical instructions for EVERY room participant (seats AND the director's
    // room-participant instance) — the room's rules/context, never the persona (that is injected
    // per-turn by RoomSeatAgent as the LAST message) and never the bounded view data itself
    // (that arrives as the group chat's opening message, shared identically by every turn's
    // history). Deliberately does NOT carry a "your output must be valid JSON" contract — unlike
    // ReelForgeAgentBase's auto-appended one for AgentType.VideoStoryEditor/VideoEditDirector's
    // OWN structured-output calls, room turns are always free-form prose.
    private const string EditRoomCharterPrompt =
        """
        You are participating in a live "edit room" discussion between several video editors and a
        director, deciding which spans of a source video to KEEP. The bounded view of the video's
        shots, silence gaps, and (when available) transcript segments — each with a short opaque id
        such as "s2", "g3", or "t7" — was given to you as the first message in this conversation.
        Every participant in this room sees the exact same view.

        ## Rules — hard constraints, not suggestions

        - Reference ONLY ids that appear in the view you were given. Never invent an id, never
          guess one.
        - NEVER mention, estimate, or output a timestamp, duration, frame number, or any other
          numeric time value, in this discussion. A separate deterministic step resolves chosen ids
          to exact times — your job here is only discussing which ids to keep, qualitatively.
        - You cannot create, request, or describe a transition, fade, dissolve, or effect of any
          kind — a separate deterministic step decides those from measurements of the footage.
        - Keep every turn SHORT — a few sentences of prose, not an essay. This is a live discussion,
          not a final report.
        - Do NOT output JSON, markdown code fences, or any structured format in this discussion —
          plain conversational prose only. A separate call, made after this discussion ends,
          produces the final structured decision.
        """;

    public EditRoomStepExecutor(
        IAgentChatClientProvider chatClients,
        IAgentToolProvider toolProvider,
        IAgentRegistry agentRegistry,
        IProjectFileWorkspace workspace,
        IWorkflowExecutionContextAccessor executionContextAccessor,
        ILogger<EditRoomStepExecutor> logger)
        : base(chatClients, toolProvider, agentRegistry, workspace, executionContextAccessor, logger)
    {
    }

    public override StepType StepType => StepType.EditRoom;

    protected override string RoomKind => "EditRoom";

    protected override string RoomDisplayName => "Edit room";

    protected override string FailureCodePrefix => "EDIT_ROOM";

    protected override string ConfigPropertyName => "EditRoomConfigJson";

    protected override string? GetConfigJson(WorkflowStep step) => step.EditRoomConfigJson;

    protected override IRoomStepConfig? DeserializeConfig(string json) =>
        JsonSerializer.Deserialize<EditRoomStepConfig>(json, ConfigJsonOptions);

    protected override HashSet<string> ExtractOfferedIds(JsonNode? root)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        JsonNode? view = root?["view"];
        if (view is null)
            return ids;

        CollectIds(view["shots"], ids);
        CollectIds(view["silences"], ids);
        CollectIds(view["segments"], ids);
        return ids;
    }

    private static void CollectIds(JsonNode? array, HashSet<string> ids)
    {
        if (array is not JsonArray arr)
            return;

        foreach (JsonNode? item in arr)
        {
            string? id = item?["id"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(id))
                ids.Add(id);
        }
    }

    protected override IReadOnlyList<string> ExtractIdMentions(string? text, IReadOnlySet<string> offeredIds) =>
        EditRoomGroupChatManager.ExtractOfferedIdMentions(text, offeredIds);

    protected override string NoOfferedIdsMessage =>
        "The resolved view offers no shot/silence/segment ids to decide over.";

    protected override string RoomCharterPrompt => EditRoomCharterPrompt;

    protected override AgentType SeatAgentType => AgentType.VideoStoryEditor;

    protected override AgentType DirectorAgentType => AgentType.VideoEditDirector;

    protected override AgentType SoloFallbackAgentType => AgentType.VideoStoryEditor;

    protected override string SeatRoleNoun => "editor";

    protected override string DirectorTurnDirective =>
        "You are now speaking as Director, moderating this edit room. Review the discussion " +
        "so far. If the room has converged on a good, coherent cut, end this turn with the " +
        "literal token ROOM_DECIDED followed by a one- or two-sentence summary of what was " +
        "agreed. Otherwise, give brief guidance to help the editors converge. Keep this turn short.";

    protected override string SynthesisSchemaInstruction =>
        "output ONLY valid JSON matching the VideoEditDecisionOutput schema.";

    protected override void NormalizeDecision(VideoEditDecisionOutput decision) =>
        decision.Keep ??= [];

    protected override string? RejectFreshSynthesis(VideoEditDecisionOutput decision) =>
        decision.Keep.Count == 0 ? "Decision had an empty Keep list." : null;

    // -----------------------------------------------------------------
    // Deterministic validation — never trust the model, same discipline VideoCompileStepExecutor
    // already applies to VideoStoryEditor's output: any Keep span whose FromId/ToId isn't in the
    // offered-id set is dropped (recorded in the output's room.droppedSpanCount).
    // -----------------------------------------------------------------

    protected override int FilterToOfferedIds(VideoEditDecisionOutput decision, HashSet<string> offeredIds)
    {
        decision.Keep ??= [];
        int before = decision.Keep.Count;
        decision.Keep = decision.Keep
            .Where(k => offeredIds.Contains(k.FromId) && offeredIds.Contains(k.ToId))
            .ToList();
        return before - decision.Keep.Count;
    }

    protected override string? RejectFilteredDecision(VideoEditDecisionOutput decision, bool solo) =>
        decision.Keep.Count != 0
            ? null
            : solo
                ? "Solo fallback produced no Keep spans referencing offered ids."
                : "The synthesized decision had no Keep spans referencing offered ids.";

    protected override RoomGroupChatManager CreateManager(
        IReadOnlyList<AIAgent> agents, IRoomStepConfig config, IReadOnlySet<string> offeredIds) =>
        new EditRoomGroupChatManager(agents, (EditRoomStepConfig)config, offeredIds, "Director");
}
