namespace ReelForge.Shared.Data.Models;

/// <summary>
/// Status of a project through its lifecycle.
/// </summary>
public enum ProjectStatus
{
    Draft,
    Processing,
    Complete,
    Failed
}

/// <summary>
/// Status of a file's AI-generated summary.
/// </summary>
public enum SummaryStatus
{
    Pending,
    Processing,
    Done,
    Failed
}

/// <summary>
/// Status of semantic/vector indexing for a project file.
/// </summary>
public enum FileIndexingStatus
{
    NotIndexed,
    Pending,
    Processing,
    Indexed,
    Failed
}

/// <summary>
/// Status of a workflow execution.
/// </summary>
public enum ExecutionStatus
{
    Queued,
    Running,
    Passed,
    Failed,
    Cancelled
}

/// <summary>
/// Type of agent available in the system.
/// </summary>
public enum AgentType
{
    CodeStructureAnalyzer,
    DependencyAnalyzer,
    ComponentInventoryAnalyzer,
    RouteAndApiAnalyzer,
    StyleAndThemeExtractor,
    RemotionComponentTranslator,
    AnimationStrategyAgent,
    DirectorAgent,
    ScriptwriterAgent,
    AuthorAgent,
    ReviewAgent,
    FileSummarizerAgent,
    Custom,
    /// <summary>
    /// Deterministic, non-LLM data extraction and projection built-in agent used by
    /// StepType.Extract steps. Never sent to a model.
    /// </summary>
    ExtractTransform,
    /// <summary>
    /// LLM agent that decides which shots/silence gaps/transcript spans to KEEP from a bounded,
    /// id-anchored view of a video analysis (StepType.VideoAnalyze output). Never emits a
    /// timestamp — see ReelForge.Shared.Data.OutputSchemas.VideoEditDecisionOutput.
    /// </summary>
    VideoStoryEditor,
    /// <summary>
    /// Deterministic, non-LLM video derushing and cutting built-in agent used by
    /// StepType.VideoAnalyze and StepType.VideoCompile steps. Runs ffmpeg, never a model.
    /// Identical role to ExtractTransform, one row serving both new deterministic step types.
    /// </summary>
    VideoTransform,
    /// <summary>
    /// LLM agent that plans zero or more motion-graphics overlays (lower-thirds, titles,
    /// callouts) anchored ONLY to opaque placement ids offered by a StepType.VideoAnalyze step
    /// (Phase 3). Never emits a coordinate or a timestamp — see
    /// ReelForge.Shared.Data.OutputSchemas.MotionGraphicsPlanOutput.
    /// </summary>
    MotionGraphicsPlanner,
    /// <summary>
    /// LLM agent used by a StepType.ReviewLoop step in the video-editing templates
    /// (video-derush-edit / video-derush-edit-graphics) to score the compiled edit and, when the
    /// score is below MinScore, loop execution back to an earlier VideoStoryEditor/
    /// MotionGraphicsPlanner step with concrete feedback. Reviews deterministic facts already
    /// computed by VideoCompileStepExecutor (transcript sentence-boundary check, overlay frame
    /// coverage) rather than judging code/lint quality like AgentType.ReviewAgent — see
    /// ReelForge.Shared.Data.OutputSchemas.VideoReviewOutput and docs/video-editing.md.
    /// </summary>
    VideoReviewAgent,
    /// <summary>
    /// LLM agent that picks a single background-music track (an offered "m{n}" id, drawn from a
    /// StepType.VideoAnalyze step's OfferMusicTracks-derived candidate list) plus enum-word
    /// choices for intensity/ducking/fit for the video-editing pipeline's optional background
    /// music (see docs/video-editing.md "Background music"). Never emits a dB value, level, or
    /// timestamp - see ReelForge.Shared.Data.OutputSchemas.MusicPlanOutput. The deterministic
    /// alternative (VideoCompileStepConfig.MusicTrackProjectFileId, set directly by the workflow
    /// author) does not require this agent at all.
    /// </summary>
    MusicSupervisor,
    /// <summary>
    /// LLM agent used TWICE by a StepType.EditRoom step: once per-turn as the moderator
    /// participant in the room's live Microsoft.Agents.AI.Workflows group chat (free-form prose,
    /// emits the literal sentinel "ROOM_DECIDED" once satisfied), and once more, OUTSIDE the group
    /// chat, for a single ordinary structured-output synthesis call that converts the room's
    /// discussion into ONE schema-validated VideoEditDecisionOutput — the exact same schema
    /// AgentType.VideoStoryEditor emits, and reused verbatim: same rushcut invariant (never a
    /// timestamp, only offered ids). See docs/video-editing.md "The edit room".
    /// </summary>
    VideoEditDirector,
    /// <summary>
    /// LLM agent used TWICE by a StepType.GraphicsRoom step — the motion-graphics analogue of
    /// AgentType.VideoEditDirector: once per-turn as the lead-artist moderator participant in the
    /// room's live group chat (free-form prose, emits the literal sentinel "ROOM_DECIDED" once
    /// satisfied), and once more, OUTSIDE the group chat, for a single ordinary structured-output
    /// synthesis call that converts the room's discussion into ONE schema-validated
    /// MotionGraphicsPlanOutput — the exact same schema AgentType.MotionGraphicsPlanner emits,
    /// reused verbatim: same extended rushcut invariant (never a timestamp OR a pixel coordinate,
    /// only offered placement ids). Unlike VideoEditDirector, this agent's synthesis call carries
    /// the same sandbox+Remotion+render tool set as MotionGraphicsPlanner (minus WriteProjectFile)
    /// so a synthesized overlay can be backed by a real rendered transparent asset — see
    /// ToolGroupCatalog's rationale. Room-participant turns are tool-restricted to read-only by
    /// GraphicsRoomStepExecutor regardless. See docs/video-editing.md "The graphics room".
    /// </summary>
    MotionGraphicsDirector,
    /// <summary>
    /// LLM agent that picks ONE whole-program colour-grade treatment for a compiled edit as
    /// enum WORDS only (a named look plus strength/shadow/highlight words — see
    /// ReelForge.Shared.Data.OutputSchemas.ColorGradePlanOutput). Never emits an RGB value, a
    /// curve/gamma/gain number, a percentage, or a timestamp — every property is a plain string
    /// (guarded by ColorGradePlanOutputInvariantTests), and VideoCompileStepExecutor alone
    /// resolves the words to concrete ffmpeg eq/colorbalance/colorlevels/hue parameters from
    /// first-party tables (ColorGradeFilterBuilder). Deliberates over the same bounded
    /// VideoAnalyze view (measured per-shot colour temperature/tone/saturation words) the story
    /// editor sees. See docs/video-editing.md "Color grading".
    /// </summary>
    Colorist,
    /// <summary>
    /// LLM agent used TWICE by a StepType.ColorGradeRoom step — the colour-grading analogue of
    /// AgentType.VideoEditDirector: once per-turn as the supervising-colorist moderator
    /// participant in the room's live group chat (free-form prose, emits the literal sentinel
    /// "ROOM_DECIDED" once satisfied), and once more, OUTSIDE the group chat, for a single
    /// ordinary structured-output synthesis call that converts the room's discussion into ONE
    /// schema-validated ColorGradePlanOutput — the exact same schema AgentType.Colorist emits,
    /// reused verbatim: same words-only invariant (never a numeric colour value). Same minimal
    /// read-only tool scope as VideoEditDirector — unlike MotionGraphicsDirector, nothing about
    /// a grade ever needs rendering. See docs/video-editing.md "The color grade room".
    /// </summary>
    ColorGradeDirector,
    /// <summary>
    /// LLM agent that plans zero or more discrete sound-effect cues (whooshes, clicks, dings,
    /// stingers) for a compiled edit — each cue an offered "x{n}" SFX-clip id (drawn from a
    /// StepType.VideoAnalyze step's OfferSfxClips-derived candidate list) anchored to an offered
    /// cut-anchor id ("s{n}"/"g{n}"/"t{n}"), plus enum-word Timing/Volume choices — see
    /// ReelForge.Shared.Data.OutputSchemas.SfxPlanOutput and docs/video-editing.md
    /// "Sound effects". Never emits a timestamp, a dB value, or a duration (guarded by
    /// SfxPlanOutputInvariantTests); VideoCompileStepExecutor alone resolves each anchor id to an
    /// output-timeline moment and each word to a concrete gain/offset. Unlike background music,
    /// there is deliberately NO deterministic no-agent config path — cue placement is inherently
    /// editorial (see docs/video-editing.md "Sound effects" for the rejected alternative).
    /// </summary>
    SoundDesigner
}

/// <summary>
/// Discriminator for workflow step behavior.
/// </summary>
public enum StepType
{
    Agent,
    Conditional,
    ForEach,
    ReviewLoop,
    /// <summary>
    /// Runs multiple agents in parallel and merges their outputs into a JSON array
    /// passed to the next step as: [{"agentName":"...","output":"{..."}}, ...]
    /// </summary>
    Parallel,
    /// <summary>
    /// Deterministic, non-LLM projection step. See ReelForge.Shared.Workflows.ExtractStepConfig.
    /// </summary>
    Extract,
    /// <summary>
    /// Deterministic, non-LLM video derush/analysis step (ffmpeg + optional ASR).
    /// See ReelForge.Shared.Workflows.VideoAnalyzeStepConfig.
    /// </summary>
    VideoAnalyze,
    /// <summary>
    /// Deterministic, non-LLM video cut/compile step (ffmpeg). Resolves an editorial
    /// decision's opaque ids to frame-accurate times against a VideoAnalyze artifact.
    /// See ReelForge.Shared.Workflows.VideoCompileStepConfig.
    /// </summary>
    VideoCompile,
    /// <summary>
    /// Multi-agent "edit room": several AgentType.VideoStoryEditor-role seats plus an
    /// AgentType.VideoEditDirector moderator converse in a live Microsoft.Agents.AI.Workflows
    /// group chat over a bounded VideoAnalyze view, then the director emits ONE schema-validated
    /// VideoEditDecisionOutput in a normal structured call outside the chat loop — exactly the
    /// shape a StepType.Agent + AgentType.VideoStoryEditor step already produces, so
    /// VideoCompileStepExecutor needs no changes to consume it. See
    /// ReelForge.Shared.Workflows.EditRoomStepConfig and docs/video-editing.md "The edit room".
    /// </summary>
    EditRoom,
    /// <summary>
    /// Multi-agent "graphics room" — the motion-graphics analogue of EditRoom, built on the same
    /// shared room infrastructure (RoomGroupChatManager/RoomStepExecutorBase): several
    /// AgentType.MotionGraphicsPlanner-role seats plus an AgentType.MotionGraphicsDirector
    /// moderator converse in a live group chat over the same offered view.placements candidates
    /// (p{n} ids) a solo planner would see, then the director emits ONE schema-validated
    /// MotionGraphicsPlanOutput in a normal structured call outside the chat loop — exactly the
    /// shape a StepType.Agent + AgentType.MotionGraphicsPlanner step already produces, so
    /// VideoCompileStepExecutor's GraphicsPlan resolution needs no changes to consume it. See
    /// ReelForge.Shared.Workflows.GraphicsRoomStepConfig and docs/video-editing.md
    /// "The graphics room".
    /// </summary>
    GraphicsRoom,
    /// <summary>
    /// Multi-agent "color grade room" — the colour-grading analogue of EditRoom/GraphicsRoom,
    /// built on the same shared room infrastructure (RoomGroupChatManager/RoomStepExecutorBase):
    /// several AgentType.Colorist-role seats plus an AgentType.ColorGradeDirector moderator
    /// converse in a live group chat over a bounded VideoAnalyze view's measured per-shot colour
    /// facts (s{n} shot ids), then the director emits ONE schema-validated ColorGradePlanOutput
    /// in a normal structured call outside the chat loop — exactly the shape a StepType.Agent +
    /// AgentType.Colorist step already produces, so VideoCompileStepExecutor's ColorGradePlan
    /// resolution needs no changes to consume it. See
    /// ReelForge.Shared.Workflows.ColorGradeRoomStepConfig and docs/video-editing.md
    /// "The color grade room".
    /// </summary>
    ColorGradeRoom
}

/// <summary>
/// Status of individual workflow step execution.
/// </summary>
public enum StepStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Skipped
}

/// <summary>
/// Controls which workflow context an Agent step receives as input.
/// </summary>
public enum AgentInputContextMode
{
    /// <summary>Uses the current accumulated workflow output.</summary>
    FullWorkflow,

    /// <summary>Uses only the most recent completed step output.</summary>
    PreviousStepOnly,

    /// <summary>Uses only a selected subset of prior step outputs.</summary>
    SelectedPriorSteps,

    /// <summary>Uses InputMappingJson to build a custom mapped subset.</summary>
    CustomMappedSubset
}

/// <summary>
/// Controls how much prior workflow context an agent receives as input.
/// </summary>
public enum ContextMode
{
    /// <summary>Only the immediately preceding step's output is passed (default).</summary>
    LastStep,
    /// <summary>All previous steps' outputs are concatenated and passed.</summary>
    AllSteps,
    /// <summary>The last N steps' outputs are concatenated (N = ContextWindowSize).</summary>
    LastN
}

/// <summary>
/// The kind of backend an <see cref="InferenceProvider"/> talks to.
/// </summary>
/// <remarks>
/// Persisted as a string (<c>.HasConversion&lt;string&gt;()</c> in both DbContexts), so adding a
/// member here needs no EF migration — the same reason <see cref="InferenceProviderCapability.Vision"/>
/// needed none.
/// </remarks>
public enum InferenceProviderKind
{
    AzureOpenAI,
    OpenAICompatible,

    /// <summary>
    /// Anthropic's first-party Messages API (<c>api.anthropic.com</c>), via the official
    /// <c>Anthropic</c> NuGet SDK. Claude speaks a different wire protocol from OpenAI's Chat
    /// Completions — this is deliberately NOT modelled as an <see cref="OpenAICompatible"/> row
    /// pointed at a translating gateway, so no proxy sits between the engine and the model.
    /// <para>
    /// Chat and <see cref="InferenceProviderCapability.Vision"/> only: Anthropic exposes no
    /// speech-to-text endpoint, so a <see cref="InferenceProviderCapability.Transcription"/> row of
    /// this kind is rejected at the API boundary rather than failing later inside a workflow.
    /// </para>
    /// </summary>
    Anthropic
}

/// <summary>
/// What an <see cref="InferenceProvider"/> row is used for. A single row's <c>ModelName</c>
/// cannot serve more than one role (a Whisper deployment is a different deployment from a chat
/// deployment, and many OpenAI-compatible chat gateways have no <c>/audio/transcriptions</c>
/// endpoint at all), so this discriminator is load-bearing, not cosmetic: it determines which
/// resolution path (chat vs. transcription vs. vision) a provider is eligible for, and which
/// "one default row" uniqueness constraint it participates in — each capability's default is
/// fully independent (a composite unique index on <c>(capability, is_default)</c>).
/// </summary>
/// <remarks>
/// <see cref="Vision"/> (Phase 2 of the video-editing feature — see docs/video-editing.md
/// "Vision captioning") reuses the exact same chat-completions machinery as <see cref="Chat"/>
/// (a vision call is just a chat call with an image content part), so it resolves via
/// <c>IInferenceProviderResolver.ResolveVisionAsync</c> into the same <c>ResolvedInferenceProvider</c>
/// type <see cref="Chat"/> uses — it is kept as a separate capability rather than folded into
/// <see cref="Chat"/> only so a Vision-capable deployment (which may differ from the agent chat
/// deployment) can be configured and defaulted independently, and so a Vision default never
/// silently answers an agent chat-completion resolution or vice versa.
/// </remarks>
public enum InferenceProviderCapability
{
    Chat,
    Transcription,
    Vision
}
