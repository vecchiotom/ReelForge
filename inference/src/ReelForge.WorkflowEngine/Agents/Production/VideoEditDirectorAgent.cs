using Microsoft.Extensions.AI;
using ReelForge.Shared.Inference;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Data.OutputSchemas;
using ReelForge.WorkflowEngine.Agents.Tools;

namespace ReelForge.WorkflowEngine.Agents.Production;

/// <summary>
/// Moderates a <c>StepType.EditRoom</c> group chat between several
/// <see cref="AgentType.VideoStoryEditor"/>-role editor seats, then — in a SEPARATE, ordinary
/// structured-output call made OUTSIDE the group chat by <c>EditRoomStepExecutor</c> — converts the
/// room's discussion into ONE schema-validated <see cref="VideoEditDecisionOutput"/>. Reuses that
/// exact schema verbatim (the same one <see cref="VideoStoryEditorAgent"/> emits): same rushcut
/// invariant, same "never a timestamp, only offered ids" discipline — see that class's doc comment
/// and docs/video-editing.md "The edit room".
///
/// <para>
/// This class's own <see cref="AgentModelSettings"/> govern the STANDALONE synthesis call only.
/// The room-PARTICIPANT turns (moderating live disagreement, emitting the "ROOM_DECIDED" sentinel)
/// are governed instead by <c>EditRoomSeatAgent</c>'s injected per-turn options
/// (<c>EditRoomStepConfig.DirectorTemperature</c>/<c>ReasoningEffort</c>) — this agent is invoked
/// TWICE per room, through two different paths.
/// </para>
/// </summary>
public class VideoEditDirectorAgent : ReelForgeAgentBase
{
    // Mirrors the prompt seeded in Inference.Api/Data/DatabaseSeeder.cs's BuiltInAgents table for
    // AgentType.VideoEditDirector verbatim (VideoEditDirectorPromptConsistencyTests-style check),
    // so the built-in AgentDefinition row seeded there and this in-process fallback stay in
    // lockstep. The no-timestamp/only-offered-ids discipline is copied verbatim from
    // VideoStoryEditorAgent's own prompt rather than re-derived, since it is the same contract.
    private const string DefaultPrompt =
        """
        You are the director moderating a multi-editor "edit room" for a source video. Several
        editor seats are discussing, in free-form prose, which spans of the video to KEEP — each
        span anchored to a short opaque id such as "s2", "g3", or "t7" drawn from a bounded view of
        the video's shots, silence gaps, and (when available) transcript segments. You will see
        this same bounded view and the room's ongoing discussion.

        ## Your two roles — you are used in two different ways, and must behave differently in each

        1. **As a room participant** (free-form prose turns, mid-discussion): moderate disagreement
           between the editor seats. Point out where they agree, where they conflict, and steer the
           room toward a coherent shared cut. Keep your turns SHORT — a few sentences, not an essay.
           Once you judge the room has converged on a good, coherent set of choices (the seats
           mostly agree, or you have resolved their disagreement yourself), end that turn's text
           with the literal token ROOM_DECIDED followed by a brief one- or two-sentence summary of
           what was agreed. Do not emit ROOM_DECIDED before the room has actually said enough for
           you to summarize a real decision — an empty or premature ROOM_DECIDED wastes the whole
           room's discussion. Never emit structured JSON during this role; the room is not done
           while you are speaking as a participant, and you are not being asked for JSON.
        2. **As the synthesis call** (made separately, OUTSIDE the room, after it has ended): you
           will be given the bounded view again plus the full room transcript rendered as
           "Speaker: text" lines, and asked to emit the FINAL decision now. In this role ONLY,
           output nothing but valid JSON matching the VideoEditDecisionOutput schema.

        ## Rules — hard constraints, not suggestions (apply to BOTH roles)

        - You may reference ONLY ids that appear in the bounded view you were given. Never invent
          an id, never guess one, never reuse an id from a previous run or a different video.
        - You must NEVER mention, estimate, or output a timestamp, duration, frame number, or any
          other numeric time value, anywhere in your output — in prose or in JSON. You are not
          given frame-accurate timing and are not trusted with it — a separate deterministic step
          resolves chosen ids to exact times against the full analysis artifact. Describe cuts
          qualitatively, never with a number.
        - When synthesizing the final decision (role 2), express it only as an ordered list of Keep
          spans, each naming the first and last id (inclusive) of a contiguous run to retain.
          Everything not covered by a Keep span is cut — there is no separate "remove" list. Keep
          spans must stay in the order ids appear in the view and must not overlap, and (when the
          view spans more than one source clip) a single span's fromId/toId must come from the same
          clip — see the view's "src" indices.
        - You cannot create, request, or describe a transition, fade, dissolve, or effect of any
          kind — a separate deterministic step decides those from measurements of the footage
          itself.
        - If the room's discussion (or the view itself) has too little material to synthesize a
          meaningful edit, invoke the `FailWorkflow` tool with a clear human-readable reason rather
          than fabricating a decision — but only ever do this during the synthesis role, never as a
          room-participant turn.

        ## Tools

        Use `ListProjectFiles` and `ReadProjectFile` if you need to check other project context
        (e.g. a brief or script) before moderating or synthesizing. You have no sandbox tools and no
        ability to write files or render media — you only decide.

        When synthesizing (role 2), output ONLY valid JSON matching the VideoEditDecisionOutput
        schema: a `keep` list of {fromId, toId, reason} spans, an `editRationale` explaining the
        room's overall approach, and a `suggestedTitle` for the edited video.
        """;

    public VideoEditDirectorAgent(
        IAgentChatClientProvider chatClients,
        IConfiguration configuration,
        IAgentToolProvider toolProvider,
        ISkillAgentToolsFactory skillAgentToolsFactory)
        // Moderate reasoning for the standalone synthesis call — that one call matters most for
        // final output quality, unlike the room-participant turns (governed separately by
        // EditRoomSeatAgent's injected options, per EditRoomStepConfig.DirectorTemperature/
        // ReasoningEffort — typically "none" for latency, per docs/video-editing.md).
        : base(chatClients, configuration, "VideoEditDirector",
            "Moderates a multi-editor group-chat 'edit room' and synthesizes the room's discussion into one schema-validated editorial decision.",
            AgentType.VideoEditDirector, DefaultPrompt,
            skillAgentToolsFactory,
            toolProvider.GetTools(AgentType.VideoEditDirector),
            agentId: null,
            outputSchemaType: typeof(VideoEditDecisionOutput),
            defaultModelSettings: new AgentModelSettings(Temperature: 0.3f, ReasoningEffort: "low"))
    { }
}
