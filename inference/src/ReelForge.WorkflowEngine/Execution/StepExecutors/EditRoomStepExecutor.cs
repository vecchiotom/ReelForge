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
/// vocabulary, its charter/director prompts, its seat/director/solo agent types, its "an empty
/// Keep list is never acceptable" validation, and its multi-source (several source clips offered
/// in one view) rule that a single Keep span may never bridge two different clips.
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
    internal const string EditRoomCharterPrompt =
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
        - Some views span MORE THAN ONE source clip — e.g. several takes or camera angles of the
          same scene. When this is the case, every id in the view also carries a "src" index (e.g.
          "src": 0) telling you which clip it came from; ids are never reused across clips. You may
          pick whichever clip has the best material for each moment and freely alternate between
          clips across successive kept runs — that is the whole point of offering more than one
          clip. The one hard rule: a single kept run's first and last id must both come from the
          SAME clip (same "src"), since a run is contiguous within one physical file — never bridge
          two different clips inside one run. Propose cross-clip edits as a SEQUENCE of single-clip
          runs instead.
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

    protected override HashSet<string> ExtractOfferedIds(JsonNode? root) =>
        new(ExtractOfferedIdSources(root).Keys, StringComparer.Ordinal);

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

    /// <summary>
    /// One retry with precise feedback for a decision whose Keep spans pair ids from two different
    /// source clips, mirroring <see cref="RejectFreshSynthesis"/>'s retry ladder for an empty Keep
    /// list. Unlike that check, this is NOT fatal on the last attempt — the base template accepts
    /// the decision regardless once attempts are exhausted, and <see cref="FilterToOfferedIds"/>
    /// drops the offending span(s) so a partially-valid final decision still compiles instead of
    /// degrading the whole room to the solo fallback.
    /// </summary>
    protected override string? CheckRetryableIssue(VideoEditDecisionOutput decision, JsonNode? viewRoot)
    {
        Dictionary<string, int> offeredIdSources = ExtractOfferedIdSources(viewRoot);
        int mixedCount = CountMixedSourceSpans(decision, offeredIdSources);
        if (mixedCount == 0)
            return null;

        return $"{mixedCount} Keep span(s) pair a fromId and toId from two different source clips " +
            "(different \"src\" values in the view). A single Keep span must start and end in the " +
            "SAME clip — express a cross-clip edit as a sequence of single-clip Keep spans instead.";
    }

    // -----------------------------------------------------------------
    // Deterministic validation — never trust the model, same discipline VideoCompileStepExecutor
    // already applies to VideoStoryEditor's output.
    // -----------------------------------------------------------------

    /// <summary>
    /// Drops any Keep span whose FromId/ToId isn't an offered id, or whose FromId/ToId come from
    /// two DIFFERENT source clips (different <c>"src"</c> — the same rule
    /// <c>VideoCompileStepExecutor</c> enforces as a hard <c>MIXED_SOURCE_SPAN</c> failure; dropping
    /// it here instead keeps the step's degrade-not-fail discipline and lets the remaining
    /// single-clip spans still compile). For a single-source view every id maps to source 0, so the
    /// mixed-source check can never fire there — behavior is identical to a plain offered-ids-only
    /// filter.
    /// </summary>
    protected override RoomFilterResult FilterToOfferedIds(VideoEditDecisionOutput decision, HashSet<string> offeredIds, JsonNode? viewRoot)
    {
        Dictionary<string, int> offeredIdSources = ExtractOfferedIdSources(viewRoot);

        decision.Keep ??= [];
        int droppedUnoffered = 0;
        int droppedMixed = 0;
        var kept = new List<VideoEditKeepSpan>(decision.Keep.Count);
        foreach (VideoEditKeepSpan span in decision.Keep)
        {
            if (span.FromId is null || span.ToId is null
                || !offeredIdSources.TryGetValue(span.FromId, out int fromSrc)
                || !offeredIdSources.TryGetValue(span.ToId, out int toSrc))
            {
                droppedUnoffered++;
                continue;
            }

            if (fromSrc != toSrc)
            {
                droppedMixed++;
                continue;
            }

            kept.Add(span);
        }

        decision.Keep = kept;

        var extraMeta = new JsonObject { ["droppedMixedSourceSpanCount"] = droppedMixed };
        return new RoomFilterResult(droppedUnoffered + droppedMixed, extraMeta);
    }

    protected override string? RejectFilteredDecision(VideoEditDecisionOutput decision, bool solo) =>
        decision.Keep.Count != 0
            ? null
            : solo
                ? "Solo fallback produced no valid Keep spans (every span referenced an unoffered id or bridged two source clips)."
                : "The synthesized decision had no valid Keep spans (every span referenced an unoffered id or bridged two source clips).";

    protected override RoomGroupChatManager CreateManager(
        IReadOnlyList<AIAgent> agents, IRoomStepConfig config, IReadOnlySet<string> offeredIds) =>
        new EditRoomGroupChatManager(agents, (EditRoomStepConfig)config, offeredIds, "Director");

    /// <summary>How many Keep spans pair two offered ids from different source clips — used to give the synthesis call precise retry feedback before <see cref="FilterToOfferedIds"/> would drop them.</summary>
    private static int CountMixedSourceSpans(
        VideoEditDecisionOutput decision, IReadOnlyDictionary<string, int> offeredIdSources) =>
        (decision.Keep ?? []).Count(k =>
            k.FromId is not null && k.ToId is not null
            && offeredIdSources.TryGetValue(k.FromId, out int fromSrc)
            && offeredIdSources.TryGetValue(k.ToId, out int toSrc)
            && fromSrc != toSrc);

    /// <summary>
    /// Every offered shot/silence/segment id mapped to the source-clip index it belongs to. The
    /// bounded view only carries a <c>"src"</c> key when the analyze step actually merged more than
    /// one source clip (see <c>VideoAnalyzeStepExecutor</c>'s ShotNode/SilenceNode/SegmentNode);
    /// an absent key means single-source and maps to index 0.
    /// </summary>
    private static Dictionary<string, int> ExtractOfferedIdSources(JsonNode? root)
    {
        var sources = new Dictionary<string, int>(StringComparer.Ordinal);
        JsonNode? view = root?["view"];
        if (view is null)
            return sources;

        CollectIdSources(view["shots"], sources);
        CollectIdSources(view["silences"], sources);
        CollectIdSources(view["segments"], sources);
        return sources;
    }

    private static void CollectIdSources(JsonNode? array, Dictionary<string, int> sources)
    {
        if (array is not JsonArray arr)
            return;

        foreach (JsonNode? item in arr)
        {
            string? id = item?["id"]?.GetValue<string>();
            if (string.IsNullOrEmpty(id))
                continue;

            int src = 0;
            if (item?["src"] is JsonValue srcValue && srcValue.TryGetValue(out int parsedSrc))
                src = parsedSrc;

            sources[id] = src;
        }
    }
}
