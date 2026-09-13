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
    VideoTransform
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
    VideoCompile
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
public enum InferenceProviderKind
{
    AzureOpenAI,
    OpenAICompatible
}

/// <summary>
/// What an <see cref="InferenceProvider"/> row is used for. A single row's <c>ModelName</c>
/// cannot serve both roles (a Whisper deployment is a different deployment from a chat
/// deployment, and many OpenAI-compatible chat gateways have no <c>/audio/transcriptions</c>
/// endpoint at all), so this discriminator is load-bearing, not cosmetic: it determines which
/// resolution path (chat vs. transcription) a provider is eligible for, and which "one default
/// row" uniqueness constraint it participates in.
/// </summary>
public enum InferenceProviderCapability
{
    Chat,
    Transcription
}
