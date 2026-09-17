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
          reorder) and must not overlap. This ordering rule applies WITHIN a single
          source clip only — see "Multiple source clips" below for what changes when
          the view spans more than one clip.
        - Prefer segments with clear, complete thoughts over fragments; prefer cutting
          silence gaps and false starts; do not keep a shot solely because it is long.
        - Never end a Keep span on a transcript segment id ("t7") whose text is cut off
          mid-sentence. Every segment carries an "endsSentence" boolean — that flag, not
          your own reading of the punctuation, is the authoritative per-segment signal
          (it is computed from the segment's full untruncated text, which the "text" you
          see may have been shortened from). When "endsSentence" is false the thought
          almost certainly continues in the NEXT transcript segment — either extend the
          span's toId to include that next segment too (if it finishes the sentence), or
          end the run one segment earlier at a point that already completes a thought.
          This applies to every Keep span, not only the last one in the whole edit.
        - BUT "endsSentence" is derived purely from trailing sentence punctuation, so it
          is only as trustworthy as the transcriber that produced the text. Before acting
          on it, check "meta.transcription.punctuated" — one entry per source clip, each
          with "src" (which clip it describes), "ratio" (the fraction of that clip's
          transcript segments that end in sentence punctuation at all) and "reliable".
          When the entry for your segment's clip says "reliable": false, that transcript
          barely punctuates anything: "endsSentence": false then tells you NOTHING about
          whether the thought is finished, and you must not pad a span with extra
          segments chasing a full stop that is never going to appear. Judge completeness
          SEMANTICALLY instead — read the segment's own text and end the run where it
          reads as a whole clause or finished thought: a segment like "we rebuilt the
          whole pipeline in about three weeks" reads complete despite having no full
          stop, while one that visibly trails off — ending on a conjunction,
          preposition, article, or an otherwise unfinished clause, e.g. "and then we" or
          "so the thing is that" — does not. When "reliable" is true, trust
          "endsSentence" exactly as described above.

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
        - "look": shots sharing a "look" id were shot under similar light with a similar grade, so
          they cut together cleanly. Prefer keeping runs of shots within a single look group,
          ESPECIALLY across different "src" clips — cutting between two look groups is visible to a
          viewer as a mismatch even when both shots are individually good. This directly qualifies
          the "freely alternate between clips" guidance below: alternate on content, but prefer the
          clip whose look matches the surrounding sequence when the material is otherwise equal.
          A shot with NO "look" id has a look unlike any other shot in this analysis. If the view's
          "meta.look.uniform" is true, every shot shares one look and no "look" ids are shown at all
          — in that case this bullet simply does not apply.
        - "char" (under "a"): what the shot's audio actually IS — "Dialogue", "Music", "Ambient",
          "Noisy", or "Silent". "Music" means the clip ALREADY carries a music bed, so laying another
          one under it would stack two pieces of music. "Noisy" means a high ambient noise floor.
          Note that "speech" is derived from silence detection, not from recognizing speech, so it is
          unreliable whenever "char" is "Music" — a musical passage reads as high "speech".

        The view may also carry a "lookGroups" list. Each entry dereferences one "look" id to a few
        descriptive words — "temp" (Warm/Neutral/Cool), "tone" (Flat/Normal/Contrasty/Crushed/Blown),
        "sat" (Muted/Natural/Vivid) — plus "cohesion" (0-100, how tightly that group holds together)
        and "repShotId" (the shot most representative of that look). "tone": "Flat" on a whole group
        usually means ungraded log footage, which is a property of the SOURCE, not a per-shot quality
        defect — do not cut a shot merely because it looks low-contrast when its whole look group does.

        Some shots also carry a "c" (caption) key: a short AI-generated description of what
        is visually happening in the shot — subjects present, the action, the setting, the
        mood, the shot scale, on-screen text, and a few tags. Use it as extra context for
        judging pacing and quality (e.g. preferring a shot whose caption suggests a clear,
        complete moment over one that sounds like a fragment or a false start), exactly like
        "v"/"a" — never as a source of timing. A shot with no "c" key is normal, not a
        signal that the shot is empty or unimportant: captioning only runs on a
        budget-limited subset of shots, so most shots will not have one.

        A caption may also carry "style" (how the shot is graded/finished) and "issues" (visible
        technical defects the analyzer cannot measure — soft focus, a blown window, banding, rolling
        shutter). Treat "issues" as a usability signal: all else equal, prefer a take without them,
        and never keep a shot with an issue purely because it is longer.

        ## Multiple source clips (when present)

        Some workflows analyze more than one source video clip in a single run — e.g.
        several takes or camera angles of the same scene. When this is the case, every id
        in the view also carries a "src" index (e.g. "src": 0) telling you which clip it
        came from; ids are never reused across clips. You may pick whichever clip has the
        best material for each moment and freely alternate between clips across successive
        Keep spans — that is the whole point of giving you more than one clip. The one hard
        rule: a single Keep span's fromId and toId must both come from the SAME clip (same
        "src"), since a span is a contiguous run within one physical file — never bridge two
        different clips inside one span. Compose the cross-clip edit as a SEQUENCE of
        single-clip Keep spans instead.

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
