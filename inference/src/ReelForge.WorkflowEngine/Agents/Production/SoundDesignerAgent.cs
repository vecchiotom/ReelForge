using Microsoft.Extensions.AI;
using ReelForge.Shared.Inference;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Data.OutputSchemas;
using ReelForge.WorkflowEngine.Agents.Tools;

namespace ReelForge.WorkflowEngine.Agents.Production;

/// <summary>
/// Plans zero or more discrete sound-effect cues (whooshes, clicks, dings, stingers) for the
/// video-editing pipeline's optional sound-effects layer (see docs/video-editing.md
/// "Sound effects"). Each cue is a pair of opaque offered ids — WHICH clip
/// (an <c>x{n}</c> id from <c>VideoAnalysisArtifact.OfferedSfxIds</c>) at WHICH moment (a
/// cut-anchor <c>s{n}</c>/<c>g{n}</c>/<c>t{n}</c> id from <c>OfferedIds</c>, the cue firing when
/// that item begins in the compiled output) — plus enum-word Timing/Volume choices.
///
/// The rushcut invariant, extended: <see cref="SfxPlanOutput"/>/<see cref="SfxCue"/> have no
/// numeric or time-bearing property at all — every property is a plain string (guarded by
/// <c>SfxPlanOutputInvariantTests</c>) — so this agent is physically incapable of emitting a
/// timestamp, a millisecond offset, a dB value, or a duration. <c>VideoCompileStepExecutor</c>
/// alone resolves each anchor id to an output-timeline moment (through the same
/// <c>OutputTimeline</c> mapping graphics overlays and music ducking use) and each word to a
/// concrete gain/offset. Unlike background music, there is deliberately no deterministic
/// no-agent config path — cue placement is inherently editorial.
/// </summary>
public class SoundDesignerAgent : ReelForgeAgentBase
{
    // Mirrors the prompt seeded in Inference.Api/Data/DatabaseSeeder.cs's BuiltInAgents table for
    // AgentType.SoundDesigner verbatim, so the built-in AgentDefinition row seeded there and this
    // in-process fallback (used only if that config-driven SystemPrompt is ever absent) stay in
    // lockstep with the same hard constraints. Enforced by VideoStoryEditorPromptConsistencyTests.
    private const string DefaultPrompt =
        """
        You are a sound designer for an edited video. You are given the story editor's
        already-decided edit plus the same bounded analysis view it was decided from — a
        list of shots ("s0", "s2"), silence gaps ("g1"), and transcript segments ("t3"),
        plus a list of candidate sound-effect clips under "sfxClips" — each with a short
        opaque id such as "x0" or "x2" and a file name. Plan ZERO OR MORE discrete
        sound-effect cues: for each cue, WHICH offered clip plays at the moment WHICH
        offered shot/gap/segment begins in the compiled edit.

        ## Rules — hard constraints, not suggestions

        - A cue's `sfxId` may reference ONLY a clip id that appears in the "sfxClips" list
          you were given. Never invent one, never guess one, and never reuse a
          shot/silence/segment/placement/music id ("s2", "g3", "t7", "p1", "m0") as a clip
          id — those are completely different kinds of id and are never valid there.
        - A cue's `anchorId` may reference ONLY a shot, silence-gap, or transcript-segment
          id that appears in the view you were given ("s{n}", "g{n}", "t{n}"). The cue
          fires when that item BEGINS in the compiled edit. Prefer anchors that the story
          editor actually KEPT — a cue anchored to a moment that was cut away is silently
          dropped, never relocated. Never anchor a cue to a clip id, a placement id, or a
          music-track id.
        - You must NEVER output, estimate, or mention a timestamp, an offset in seconds or
          milliseconds, a duration, a volume, a decibel (dB) value, or a percentage,
          anywhere in your structured output. You are not given, and are not trusted with,
          any of that — a separate deterministic step resolves your anchor ids to exact
          output-timeline moments and your enum-word choices to actual gains and offsets.
        - Timing is a WORD, not a number: choose exactly one of "OnCut" (the cue fires
          exactly as the anchor begins — right for whooshes and transition stingers at the
          start of a new shot), "Lead" (slightly before it — right for a riser into a
          moment), or "Lag" (slightly after it — right for a UI click or ding reacting to
          something just shown). A separate deterministic step maps these words to actual
          offsets — you never supply a number yourself.
        - Volume is also a WORD: choose one of "Subtle", "Normal", or "Strong". Prefer
          "Subtle" or "Normal" — especially while people are speaking — and reserve
          "Strong" for a cue that genuinely carries the moment, with the reason stated.
        - You are choosing clips on their FILE NAME and the surrounding project context
          only — you are not given their actual sound. Prefer names that read as short
          one-shot effects (whoosh, click, ding, pop, sting, impact, riser) and never plan
          a cue from a name that reads as a music bed or a long ambience — beds belong to
          the separate background-music layer, not here.
        - Be sparing. A few well-placed cues beat one on every cut — a cue must mark a
          genuine moment (a section change, a reveal, an emphasized beat), never mere
          decoration. If no offered clip suits the edit, or the edit needs no effects,
          output an EMPTY `cues` list rather than forcing one — no sound effects is a
          perfectly good outcome.

        ## Tools

        Use `ListProjectFiles` and `ReadProjectFile` if you need to check other project
        context (e.g. a brief or script) before deciding. You have no sandbox tools and no
        ability to write files or render media — you only decide.

        Output ONLY valid JSON matching the SfxPlanOutput schema: a `cues` list of
        {sfxId, anchorId, timing, volume, reason} entries (possibly empty), and a
        `planRationale` explaining your overall approach.
        """;

    public SoundDesignerAgent(
        IAgentChatClientProvider chatClients,
        IConfiguration configuration,
        IAgentToolProvider toolProvider,
        ISkillAgentToolsFactory skillAgentToolsFactory)
        // A bounded pick-from-offered-ids decision plus enum settings, minimal read-only tool
        // scope, same reasoning as VideoStoryEditorAgent/MusicSupervisorAgent: disable reasoning
        // rather than rely on the soft "low" prompt steer, which has no enforced cap.
        : base(chatClients, configuration, "SoundDesigner",
            "Plans zero or more discrete sound-effect cues (an offered clip at an offered moment, plus timing/volume words) for the video-editing pipeline's optional sound-effects layer.",
            AgentType.SoundDesigner, DefaultPrompt,
            skillAgentToolsFactory,
            toolProvider.GetTools(AgentType.SoundDesigner),
            agentId: null,
            outputSchemaType: typeof(SfxPlanOutput),
            defaultModelSettings: new AgentModelSettings(Temperature: 0.3f, ReasoningEffort: "none"))
    { }
}
