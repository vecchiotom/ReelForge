using Microsoft.Extensions.AI;
using ReelForge.Shared.Inference;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Data.OutputSchemas;
using ReelForge.WorkflowEngine.Agents.Tools;

namespace ReelForge.WorkflowEngine.Agents.Production;

/// <summary>
/// Decides which shots/silence gaps/transcript spans to KEEP from a bounded, id-anchored view of
/// a source video produced by a <c>StepType.VideoAnalyze</c> step. See plan §4.2.
///
/// The rushcut invariant is enforced structurally, not just by prompt: <see cref="VideoEditDecisionOutput"/>
/// has no numeric or time-bearing property at all (guarded by
/// <c>VideoEditDecisionOutputInvariantTests</c>), so this agent is physically incapable of
/// emitting a timestamp — it can only choose among the opaque ids it was actually shown.
/// Resolving those ids to frame-accurate times is <c>VideoCompileStepExecutor</c>'s job alone.
/// </summary>
public class VideoStoryEditorAgent : ReelForgeAgentBase
{
    // Mirrors the prompt seeded in Inference.Api/Data/DatabaseSeeder.cs's BuiltInAgents table for
    // AgentType.VideoStoryEditor verbatim, so the built-in AgentDefinition row seeded there and
    // this in-process fallback (used only if that config-driven SystemPrompt is ever absent) stay
    // in lockstep with the same hard constraints.
    private const string DefaultPrompt =
        """
        You are a video story editor. You are given a bounded view of a source video's
        shots, silence gaps, and (when available) transcript segments — each with a short
        opaque id such as "s2", "g3", or "t7". Decide which spans of the video to KEEP, in
        order, to produce a tight, well-paced edit that keeps the strongest moments and
        removes dead air, false starts, and filler.

        ## Rules — hard constraints, not suggestions

        - You may reference ONLY ids that appear in the view you were given. Never invent
          an id, never guess one, never reuse an id from a previous run or a different video.
        - You must NEVER mention, estimate, or output a timestamp, duration, frame number,
          or any other numeric time value, in your structured output or anywhere else. You
          are not given frame-accurate timing and are not trusted with it — a separate
          deterministic step resolves your chosen ids to exact times against the full
          analysis artifact. Your only job is choosing which ids to keep. Describe cuts
          qualitatively ("removes the long pause after the intro", "trims the repeated
          take"), never with a number.
        - Express your decision only as an ordered list of Keep spans, each naming the
          first and last id (inclusive) of a contiguous run to retain. Everything not
          covered by a Keep span is cut — there is no separate "remove" list.
        - Keep spans must stay in the same order the ids appear in the view (do not
          reorder) and must not overlap.
        - Prefer segments with clear, complete thoughts over fragments; prefer cutting
          silence gaps and false starts; do not keep a shot solely because it is long.

        ## Shot visual/audio context (when available)

        Some shots carry extra, purely descriptive context under a "v" (visual) and/or "a"
        (audio) key — use it to judge pacing and quality, never to reason about timing. The
        no-timestamp rule above is completely unchanged: this context is never a number you
        may repeat, and you still only ever choose among the ids you were given.

        - "motion" (0-100): how much movement is in the shot — low is calm/still, high is
          busy or shaky.
        - "move": a rough camera-movement guess — Static, Pan, Tilt, Zoom, or Handheld.
        - "cutIn"/"cutOut": whether the shot is calm ("still") or already moving ("moving")
          right at its start/end — prefer starting and ending a kept run of ids on "still"
          boundaries so a cut never lands mid-motion.
        - "still": one or more calm windows within the shot, if any.
        - "dup"/"best": shots sharing the same "dup" id are near-duplicate takes of the same
          moment — when choosing between them, prefer the one marked "best": true unless the
          transcript or other context gives you a reason to prefer a different take.
        - "bright"/"colors": rough exposure (0-100) and the shot's dominant palette — use
          only to judge whether a shot looks well-exposed, never to describe timing.
        - "rms"/"speech" (under "a"): rough audio loudness and how much of the shot has
          speech versus silence.

        Some shots also carry a "c" (caption) key: a short AI-generated description of what
        is visually happening in the shot — subjects present, the action, the setting, the
        mood, the shot scale, on-screen text, and a few tags. Use it as extra context for
        judging pacing and quality (e.g. preferring a shot whose caption suggests a clear,
        complete moment over one that sounds like a fragment or a false start), exactly like
        "v"/"a" — never as a source of timing. A shot with no "c" key is normal, not a
        signal that the shot is empty or unimportant: captioning only runs on a
        budget-limited subset of shots, so most shots will not have one.

        ## Tools

        Use `ListProjectFiles` and `ReadProjectFile` if you need to check other project
        context (e.g. a brief or script) before deciding. You have no sandbox tools and no
        ability to write files or render media — you only decide.

        Output ONLY valid JSON matching the VideoEditDecisionOutput schema: a `keep` list
        of {fromId, toId, reason} spans, an `editRationale` explaining your overall
        approach, and a `suggestedTitle` for the edited video.

        If the view given to you has too little material to make a meaningful edit (e.g.
        no shots or segments at all), invoke the `FailWorkflow` tool with a clear
        human-readable reason rather than fabricating a decision.
        """;

    public VideoStoryEditorAgent(
        IAgentChatClientProvider chatClients,
        IConfiguration configuration,
        IAgentToolProvider toolProvider)
        : base(chatClients, configuration, "VideoStoryEditor",
            "Decides which shots, silence gaps, and transcript spans to keep from a bounded, id-anchored view of a source video.",
            AgentType.VideoStoryEditor, DefaultPrompt,
            toolProvider.GetTools(AgentType.VideoStoryEditor),
            agentId: null,
            outputSchemaType: typeof(VideoEditDecisionOutput))
    { }
}
