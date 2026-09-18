using Microsoft.Extensions.AI;
using ReelForge.Shared.Inference;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Data.OutputSchemas;
using ReelForge.WorkflowEngine.Agents.Tools;

namespace ReelForge.WorkflowEngine.Agents.Production;

/// <summary>
/// Picks ONE whole-program colour-grade treatment for a compiled edit, as enum WORDS only, from a
/// bounded <c>VideoAnalyze</c> view's measured per-shot colour facts (see docs/video-editing.md
/// "Color grading"). Entirely optional: <c>VideoCompileStepConfig.EnableColorGrade</c> defaults
/// off, and even when on, a plan whose <c>Look</c> is <c>"None"</c> is a valid decision to apply
/// no grade at all.
///
/// The rushcut invariant, extended: <see cref="ColorGradePlanOutput"/> has no numeric or
/// time-bearing property at all — every property is a plain string (guarded by
/// <c>ColorGradePlanOutputInvariantTests</c>) — so this agent is physically incapable of emitting
/// an RGB value, a curve point, a gamma/gain/contrast number, a percentage, or a timestamp. It
/// can only choose among a fixed set of named looks and strength/tone words that
/// <c>VideoCompileStepExecutor</c> alone resolves to concrete ffmpeg filter parameters from
/// first-party tables (<c>ColorGradeFilterBuilder</c>).
/// </summary>
public class ColoristAgent : ReelForgeAgentBase
{
    // Mirrors the prompt seeded in Inference.Api/Data/DatabaseSeeder.cs's BuiltInAgents table for
    // AgentType.Colorist verbatim, so the built-in AgentDefinition row seeded there and this
    // in-process fallback (used only if that config-driven SystemPrompt is ever absent) stay in
    // lockstep with the same hard constraints. Enforced by VideoStoryEditorPromptConsistencyTests.
    private const string DefaultPrompt =
        """
        You are a colorist for an edited video. You are given a bounded analysis view of the
        source footage — a list of shots, each with a short opaque id such as "s0" or "s2"
        and, when available, measured visual descriptors in WORDS (colour temperature, tone,
        saturation, exposure, a look-group cohesion summary). Decide ONE colour-grade
        treatment for the WHOLE compiled edit, described entirely in the words below.

        ## Rules — hard constraints, not suggestions

        - You must NEVER output, estimate, or mention an RGB value, a hex colour, a curve or
          level number, a gamma/gain/lift/contrast/saturation value, a percentage, or a
          timestamp, anywhere in your structured output. You are not given, and are not
          trusted with, any of that — a separate deterministic step resolves your enum-word
          choices to actual ffmpeg filter parameters from first-party tables. Your only job
          is choosing the WORDS below.
        - Look is a WORD, not a value: choose exactly one of "None", "Warm", "Cool",
          "Filmic", "Vibrant", "Muted", or "Mono". Choose "None" whenever the footage is
          already well exposed and consistent — no grade is a perfectly good outcome, and a
          grade must never be decoration.
        - Strength is also a WORD: one of "Subtle", "Normal", or "Strong". Prefer "Subtle"
          or "Normal" — "Strong" needs a genuine reason stated in `reason`.
        - ShadowTone is a WORD: one of "Neutral", "Lifted", or "Deepened". HighlightTone is
          a WORD: one of "Neutral", "Softened", or "Brightened". Choose non-Neutral tones
          only when the measured descriptors actually support it (e.g. crushed shadows on
          several shots warrant "Lifted").
        - Ground every choice in the view's MEASURED words — the per-shot temperature/tone/
          saturation descriptors and look groups — referencing shot ids ("s2", "s4") in your
          prose reasoning where helpful. Never invent a measurement the view does not carry,
          and remember ONE grade must suit every kept shot, not just the best one.
        - The startSec/endSec values on each shot are for READING only — to tell shots
          apart. Never echo, adjust, or derive a number from them.

        ## Tools

        Use `ListProjectFiles` and `ReadProjectFile` if you need to check other project
        context (e.g. a brief describing the intended mood) before deciding. You have no
        sandbox tools and no ability to write files or render media — you only decide.

        Output ONLY valid JSON matching the ColorGradePlanOutput schema: `look`, `strength`,
        `shadowTone`, `highlightTone` (the enum words above), `reason` explaining this
        specific choice against the measured facts, and `planRationale` explaining your
        overall approach.
        """;

    public ColoristAgent(
        IAgentChatClientProvider chatClients,
        IConfiguration configuration,
        IAgentToolProvider toolProvider,
        ISkillAgentToolsFactory skillAgentToolsFactory)
        // A bounded pick-words-from-fixed-lists decision, minimal read-only tool scope, same
        // reasoning as VideoStoryEditorAgent/MusicSupervisorAgent: disable reasoning rather
        // than rely on the soft "low" prompt steer, which has no enforced cap.
        : base(chatClients, configuration, "Colorist",
            "Picks one whole-program colour-grade treatment (or none) as enum words for the video-editing pipeline's optional colour grading.",
            AgentType.Colorist, DefaultPrompt,
            skillAgentToolsFactory,
            toolProvider.GetTools(AgentType.Colorist),
            agentId: null,
            outputSchemaType: typeof(ColorGradePlanOutput),
            defaultModelSettings: new AgentModelSettings(Temperature: 0.3f, ReasoningEffort: "none"))
    { }
}
