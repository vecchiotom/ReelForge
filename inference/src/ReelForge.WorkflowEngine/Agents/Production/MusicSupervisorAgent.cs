using Microsoft.Extensions.AI;
using ReelForge.Shared.Inference;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Data.OutputSchemas;
using ReelForge.WorkflowEngine.Agents.Tools;

namespace ReelForge.WorkflowEngine.Agents.Production;

/// <summary>
/// Picks a single background-music track and coarse enum-word settings for the video-editing
/// pipeline's optional background music (see docs/video-editing.md "Background music"). Entirely
/// optional: the deterministic path (<c>VideoCompileStepConfig.MusicTrackProjectFileId</c>, set
/// directly by the workflow author) delivers the whole capability without this agent at all — this
/// agent only adds a genuinely editorial layer (choosing AMONG uploaded tracks, and how
/// prominent/ducked the bed should be) on top of it.
///
/// The rushcut invariant, extended: <see cref="MusicPlanOutput"/> has no numeric or time-bearing
/// property at all — every property is a plain string (guarded by
/// <c>MusicPlanOutputInvariantTests</c>) — so this agent is physically incapable of emitting a dB
/// value, a volume, a level, a percentage, or a timestamp. It can only choose among the opaque
/// music-track ids it was actually shown (<c>VideoAnalysisArtifact.OfferedMusicIds</c>) plus a
/// handful of enum-word choices (Intensity/Ducking/Fit) that <c>VideoCompileStepExecutor</c> alone
/// resolves to concrete dB levels/ffmpeg behavior.
/// </summary>
public class MusicSupervisorAgent : ReelForgeAgentBase
{
    // Mirrors the prompt seeded in Inference.Api/Data/DatabaseSeeder.cs's BuiltInAgents table for
    // AgentType.MusicSupervisor verbatim, so the built-in AgentDefinition row seeded there and this
    // in-process fallback (used only if that config-driven SystemPrompt is ever absent) stay in
    // lockstep with the same hard constraints. Enforced by VideoStoryEditorPromptConsistencyTests.
    private const string DefaultPrompt =
        """
        You are a music supervisor for an edited video. You are given the story editor's
        already-decided edit (or the same bounded analysis view) plus a list of candidate
        background-music tracks under "musicTracks" — each with a short opaque id such as
        "m0" or "m2" and a file name. Pick AT MOST ONE track to use as a background bed for
        the whole edit, plus a few coarse settings for how it should behave.

        ## Rules — hard constraints, not suggestions

        - You may reference ONLY a track id that appears in the "musicTracks" list you were
          given. Never invent one, never guess one, never reuse an id from a previous run
          or a different project, and never reuse a shot/silence/segment/placement id
          ("s2", "g3", "t7", "p1") as a music-track id — those are a completely different
          kind of id and are never valid here.
        - You must NEVER output, estimate, or mention a volume, a decibel (dB) value, a
          loudness level, a percentage, a timestamp, or a duration in seconds/milliseconds,
          anywhere in your structured output. You are not given, and are not trusted with,
          any of that — a separate deterministic step resolves your enum-word choices to
          actual dB levels and ffmpeg behavior. Your only job is choosing a track (or none)
          and describing it with the WORDS below.
        - Intensity is a WORD, not a number: choose exactly one of "Quiet", "Balanced", or
          "Feature" for how prominent the music bed should sit relative to dialogue. A
          separate deterministic step maps these words to actual bed levels — you never
          supply a number yourself.
        - Ducking is also a WORD: choose one of "Off", "Light", "Normal", or "Heavy" for how
          much the bed should duck down under dialogue. Prefer "Normal" or "Heavy" — and
          "Quiet"/"Balanced" intensity over "Feature" — whenever the edit is dialogue-heavy
          (long transcript segments, few or short silence gaps), so the music never competes
          with what is being said.
        - Fit is also a WORD: choose one of "LoopToFit" (the track repeats to fill the whole
          edit) or "PlayOnce" (the track plays once and the bed simply ends if it is shorter
          than the edit). Prefer "LoopToFit" unless the track's own file name suggests it is
          a one-shot cue (e.g. a sting or stinger) rather than a loopable bed.
        - You are choosing on the track's FILE NAME and the surrounding project context only
          — you are not given its actual duration, tempo, or any audio content. Do not
          guess or invent details about how the track sounds beyond what its name and any
          project context (a brief, a script) reasonably suggest.
        - If no tracks are offered at all, or none of the offered tracks suit this edit,
          output an EMPTY `trackId` rather than inventing one or forcing a poor fit — no
          music is a perfectly good outcome.

        ## Tools

        Use `ListProjectFiles` and `ReadProjectFile` if you need to check other project
        context (e.g. a brief or script) before deciding. You have no sandbox tools and no
        ability to write files or render media — you only decide.

        Output ONLY valid JSON matching the MusicPlanOutput schema: `trackId` (an offered
        id, or empty), `intensity`, `ducking`, `fit` (the enum words above), `reason`
        explaining this specific choice, and `planRationale` explaining your overall
        approach.
        """;

    public MusicSupervisorAgent(
        IAgentChatClientProvider chatClients,
        IConfiguration configuration,
        IAgentToolProvider toolProvider)
        // A bounded pick-one-of-N-offered-tracks decision plus enum settings, minimal read-only
        // tool scope, same reasoning as VideoStoryEditorAgent: disable reasoning rather than
        // rely on the soft "low" prompt steer, which has no enforced cap.
        : base(chatClients, configuration, "MusicSupervisor",
            "Picks a single background-music track (or none) plus intensity/ducking/fit settings for the video-editing pipeline's optional background music.",
            AgentType.MusicSupervisor, DefaultPrompt,
            toolProvider.GetTools(AgentType.MusicSupervisor),
            agentId: null,
            outputSchemaType: typeof(MusicPlanOutput),
            defaultModelSettings: new AgentModelSettings(Temperature: 0.3f, ReasoningEffort: "none"))
    { }
}
