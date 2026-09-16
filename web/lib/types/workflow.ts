export interface WorkflowDefinition {
  id: string;
  name: string;
  createdAt: string;
  updatedAt: string;
  steps: WorkflowStep[];
  requiresUserInput: boolean;
}

export type StepType = 'Agent' | 'Conditional' | 'ForEach' | 'ReviewLoop' | 'Parallel' | 'Extract' | 'VideoAnalyze' | 'VideoCompile';
export type StepStatus = 'Pending' | 'Running' | 'Completed' | 'Failed' | 'Skipped';
export type AgentInputContextMode =
  | 'FullWorkflow'
  | 'PreviousStepOnly'
  | 'SelectedPriorSteps'
  | 'CustomMappedSubset';

export interface WorkflowStep {
  id: string;
  agentDefinitionId: string;
  stepOrder: number;
  label: string;
  edgeConditionJson?: string | null;
  stepType?: StepType;
  conditionExpression?: string | null;
  loopSourceExpression?: string | null;
  loopTargetStepOrder?: number | null;
  maxIterations?: number;
  minScore?: number | null;
  inputMappingJson?: string | null;
  agentInputContextMode?: AgentInputContextMode | null;
  selectedPriorStepOrdersJson?: string | null;
  trueBranchStepOrder?: string | null;
  falseBranchStepOrder?: string | null;
  /** JSON array of AgentDefinition GUIDs to run in parallel (Parallel step type only). */
  parallelAgentIdsJson?: string | null;
  /** JSON-serialized ExtractStepConfig (Extract step type only). Deserialize with JSON.parse. */
  extractConfigJson?: string | null;
  /** JSON-serialized VideoAnalyzeStepConfig (VideoAnalyze step type only). Deserialize with JSON.parse. */
  videoAnalyzeConfigJson?: string | null;
  /** JSON-serialized VideoCompileStepConfig (VideoCompile step type only). Deserialize with JSON.parse. */
  videoCompileConfigJson?: string | null;
}

export interface CreateWorkflowRequest {
  name: string;
  steps: CreateWorkflowStepRequest[];
  requiresUserInput?: boolean;
}

export interface CreateWorkflowStepRequest {
  agentDefinitionId: string;
  stepOrder: number;
  label: string;
  stepType?: StepType;
  conditionExpression?: string | null;
  loopSourceExpression?: string | null;
  loopTargetStepOrder?: number | null;
  maxIterations?: number;
  minScore?: number | null;
  inputMappingJson?: string | null;
  agentInputContextMode?: AgentInputContextMode | null;
  selectedPriorStepOrdersJson?: string | null;
  trueBranchStepOrder?: string | null;
  falseBranchStepOrder?: string | null;
  /** JSON array of AgentDefinition GUIDs to run in parallel (Parallel step type only). */
  parallelAgentIdsJson?: string | null;
  /** JSON-serialized ExtractStepConfig (Extract step type only). */
  extractConfigJson?: string | null;
  /** JSON-serialized VideoAnalyzeStepConfig (VideoAnalyze step type only). */
  videoAnalyzeConfigJson?: string | null;
  /** JSON-serialized VideoCompileStepConfig (VideoCompile step type only). */
  videoCompileConfigJson?: string | null;
}

export interface UpdateWorkflowRequest {
  name?: string;
  steps: CreateWorkflowStepRequest[];
  requiresUserInput?: boolean;
}

export interface WorkflowExecution {
  id: string;
  workflowDefinitionId: string;
  status: 'Queued' | 'Running' | 'Passed' | 'Failed' | 'Cancelled';
  startedAt: string | null;
  completedAt: string | null;
  iterationCount: number;
  resultJson: string | null;
  correlationId?: string;
  errorMessage?: string | null;
  stepResults: WorkflowStepResult[];
  reviewScores: ReviewScore[];
  userRequest?: string | null;
}

export interface WorkflowTemplateSummary {
  key: string;
  name: string;
  description: string;
  version: number;
  autoCreateOnProject: boolean;
  requiresUserInput: boolean;
  stepCount: number;
}

export interface WorkflowStepResult {
  id: string;
  workflowStepId: string;
  output: string;
  tokensUsed: number;
  durationMs: number;
  executedAt: string;
  status?: StepStatus;
  inputJson?: string | null;
  outputJson?: string | null;
  errorDetails?: string | null;
  iterationNumber?: number | null;
  completedAt?: string | null;
  outputStorageKey?: string | null;
  /** Storage key of a large step artifact (e.g. a VideoAnalyze full analysis JSON or a VideoCompile EDL), separate from outputStorageKey so the execution UI never tries to play it as a video. */
  artifactStorageKey?: string | null;
}

export interface ReviewScore {
  id: string;
  iterationNumber: number;
  score: number;
  comments: string;
  createdAt: string;
}

/**
 * Extract step configuration. Mirrors the backend `ExtractStepConfig` record
 * (ReelForge.Shared/Workflows/ExtractStepConfig.cs) field-for-field in camelCase.
 * Serialized to `WorkflowStep.extractConfigJson` / `CreateWorkflowStepRequest.extractConfigJson`.
 */
export type ExtractOperation = 'Project' | 'Resolve' | 'Files';
export type ExtractInputSource = 'Previous' | 'Step' | 'Accumulated' | 'ProjectFiles';
export type ExtractUnknownIdBehaviour = 'Fail' | 'Skip';

export interface ExtractInputRef {
  from: ExtractInputSource;
  stepOrder?: number | null;
}

export interface ExtractExpectation {
  requiredPaths?: string[] | null;
  minItems?: number | null;
  maxItems?: number | null;
  nonEmptyStringPaths?: string[] | null;
}

export interface ExtractStepConfig {
  version: number;
  operation: ExtractOperation;
  inputs: Record<string, ExtractInputRef>;
  // -- project --
  path?: string | null;
  fields?: string[] | null;
  idField?: string | null;
  idPrefix: string;
  sortBy?: string | null;
  take?: number | null;
  skip: number;
  // -- resolve --
  idsPath?: string | null;
  recordsPath?: string | null;
  onUnknownId: ExtractUnknownIdBehaviour;
  // -- files --
  categories?: string[] | null;
  includeExtensions?: string[] | null;
  excludePathContains?: string[] | null;
  includeSummaries: boolean;
  includeContent: boolean;
  maxCharsPerFile: number;
  // -- universal --
  maxOutputChars: number;
  expect?: ExtractExpectation | null;
}

/**
 * Video editing step configuration. Mirrors the backend `VideoSource.cs`, `VideoAnalyzeStepConfig.cs`,
 * `VideoCompileStepConfig.cs` (ReelForge.Shared/Workflows/) and the `VideoEditDecisionOutput` schema
 * (ReelForge.Shared/Data/OutputSchemas.cs) field-for-field in camelCase. Enum values are PascalCase
 * string literals matching the C# member names exactly (see `ExtractOperation` above for precedent —
 * commit 8e4b6a5a fixed exactly this casing mismatch for Extract).
 */
export type VideoSourceKind = 'ProjectFile' | 'StepOutput' | 'PreviousStepOutput';

export interface VideoSourceRef {
  kind: VideoSourceKind;
  /** Kind=ProjectFile -> project_files id. */
  projectFileId?: string | null;
  /** Kind=StepOutput -> that step's stepOrder. Ignored for PreviousStepOutput. */
  stepOrder?: number | null;
}

export type VideoTranscriptionMode = 'Off' | 'Optional' | 'Required';

export interface VideoAnalyzeExpectation {
  minShots?: number | null;
  minTranscriptSegments?: number | null;
  maxSilenceRatio?: number | null;
  minShotsWithVisuals?: number | null;
}

/**
 * How much per-shot visual/audio detail (Phase 1 descriptors) the bounded view includes per shot.
 * `VideoAnalyzeStepExecutor.BuildBoundedView` degrades Full -> Compact -> None to fit
 * `maxOutputChars` BEFORE ever dropping an offered shot/silence/segment.
 */
export type VideoVisualDetail = 'None' | 'Compact' | 'Full';

export type VideoVisionMode = 'Off' | 'Optional' | 'Required';
export type VideoCaptionSelection = 'PerDuplicateGroup' | 'LongestShots' | 'EvenlySpaced';

export interface VideoAnalyzeStepConfig {
  version: number;
  source: VideoSourceRef;
  /** Multi-source analysis. A one-element list behaves identically to a single `source`. Optional — omitted for the (still overwhelmingly common) single-source case. */
  sources?: VideoSourceRef[] | null;
  // -- silence detection --
  detectSilence: boolean;
  silenceThresholdDb: number;
  minSilenceMs: number;
  // -- shot/scene detection --
  detectShots: boolean;
  sceneThreshold: number;
  // -- transcription --
  transcription: VideoTranscriptionMode;
  transcriptionProviderId?: string | null;
  language?: string | null;
  wordTimestamps: boolean;
  maxAsrChunkBytes: number;
  // -- guardrails, checked before any decode --
  maxDurationSeconds: number;
  maxInputBytes: number;
  // -- prompt-view budget --
  maxOutputChars: number;
  maxViewSegments: number;
  maxSegmentTextChars: number;
  // -- Phase 1: visual scene analysis (one low-res raw-frame grid ffmpeg pass + pure C#) --
  analyzeVisuals: boolean;
  visualSampleFps: number;
  visualGridWidth: number;
  visualGridHeight: number;
  maxVisualSampleFrames: number;
  stillMotionThreshold: number;
  minStillWindowMs: number;
  maxStillWindowsPerShot: number;
  /** Now implemented and free from the grid — defaults true. See docs/video-editing.md; under-reports on soft/gradient letterbox edges (§4.3). */
  detectLetterbox: boolean;
  /** One extra ffmpeg invocation per measured shot (capped by `maxSharpnessShots`) — off by default, unlike the other free Phase 1/4 dimensions. */
  detectSharpness: boolean;
  // -- Phase 1: audio loudness (reuses the WAV already extracted for transcription, or extracts it) --
  analyzeAudioLevels: boolean;
  // -- Phase 1: near-duplicate / best-take grouping --
  detectNearDuplicates: boolean;
  duplicateSimilarityThreshold: number;
  duplicateWindowShots: number;
  // -- Phase 1: bounded-view detail level --
  visualDetail: VideoVisualDetail;
  maxViewDuplicateGroups: number;
  // -- Phase 4: semantic visual dimensions (D1-D4/D6 are free, derived from the same grid data) --
  /** Gates D1-D3 (colour temperature, tone curve, saturation character). Free. */
  analyzeColorGrading: boolean;
  /** Gates D4 look grouping (`k{n}`). Free. */
  detectLookGroups: boolean;
  /** Minimum look similarity (0..1) for two shots to share a look group. */
  lookSimilarityThreshold: number;
  /** Caps `view.lookGroups`. */
  maxViewLookGroups: number;
  /** Frames combined into one contact-sheet keyframe per captioned shot (clamped 1..3 server-side). 1 = a single mid-shot still, byte-identical to the pre-Phase-4 vision path. */
  keyframesPerShot: number;
  /** Step-wide ceiling on sharpness measurements when `detectSharpness` is on. */
  maxSharpnessShots: number;
  expect?: VideoAnalyzeExpectation | null;
  // -- Phase 2: vision-LLM shot captioning (default Off) — optional so createDefaultVideoAnalyzeStepConfig need not enumerate them --
  vision?: VideoVisionMode;
  visionProviderId?: string | null;
  captionSelection?: VideoCaptionSelection;
  maxCaptionedShots?: number;
  minCaptionShotSeconds?: number;
  keyframeMaxWidth?: number;
  visionTimeoutSeconds?: number;
  maxCaptionChars?: number;
  /** Persists each captioned shot's keyframe JPEG to storage under the video-analysis prefix. */
  persistKeyframes?: boolean;
  // -- Phase 3: motion-graphics overlay placement candidates --
  emitOverlayPlacements?: boolean;
  maxPlacementsPerShot?: number;
  maxPlacements?: number;
  maxTimeSlicesPerRegion?: number;
  // -- background-music candidate offering --
  offerMusicTracks?: boolean;
  maxMusicTracks?: number;
}

export type VideoCompileMode = 'Reencode' | 'StreamCopy';

export interface VideoCompileExpectation {
  minOutputSeconds?: number | null;
  maxOutputSeconds?: number | null;
  minRetainedRatio?: number | null;
  maxRetainedRatio?: number | null;
}

export interface VideoCompileStepConfig {
  version: number;
  /** Only `from: 'Previous'` or `from: 'Step'` are valid here (validated server-side). Reuses ExtractInputRef verbatim. */
  decision: ExtractInputRef;
  /** Which VideoAnalyze step's full artifact to resolve ids against. */
  analysisStepOrder: number;
  /** Cross-execution override: resolve a prior run's artifact instead of this execution's. */
  analysisStepResultId?: string | null;
  mode: VideoCompileMode;
  prePaddingMs: number;
  postPaddingMs: number;
  minSegmentMs: number;
  maxSegments: number;
  /** Required=true whenever mode is StreamCopy. */
  allowKeyframeSnapping: boolean;
  outputFileName: string;
  // Allowlisted at execution time (argv-injection surface) — constrain to selects in the UI, never free text.
  videoCodec: string;
  audioCodec: string;
  crf: number;
  preset: string;
  registerProjectFile: boolean;
  // -- Phase 3: optional motion-graphics overlays (see docs/video-editing.md
  //    "Motion graphics (Phase 3)"). enableGraphics=false (default) is byte-identical to the
  //    pre-Phase-3 compile path. --
  /** Which step's resolved MotionGraphicsPlanOutput to apply. `null` = no graphics plan looked up. Only `from: 'Previous'` or `from: 'Step'` are valid, same as `decision`. */
  graphicsPlan?: ExtractInputRef | null;
  enableGraphics: boolean;
  maxOverlays: number;
  overlayShortMs: number;
  overlayMediumMs: number;
  overlayHoldMs: number;
  overlayFadeMs: number;
  /** Percent of frame height, clamped 2..12 server-side. */
  overlayFontSizePct: number;
  // Allowlisted at execution time, same discipline as videoCodec/audioCodec/preset above.
  overlayFontColor: string;
  overlayBoxColor: string;
  maxOverlayTextChars: number;
  maxOverlaySubtextChars: number;
  expect?: VideoCompileExpectation | null;
}

/**
 * Structured output produced by the VideoStoryEditor agent. Mainly useful for typing the
 * execution-page decision viewer — the model produces this, there is no form for it.
 * THE RUSHCUT INVARIANT: no numeric/time-bearing field anywhere here — ids only.
 */
export interface VideoEditKeepSpan {
  fromId: string;
  toId: string;
  reason: string;
}

export interface VideoEditDecisionOutput {
  keep: VideoEditKeepSpan[];
  editRationale: string;
  suggestedTitle: string;
}

export function getDefaultAgentInputContextMode(agentType: string | null | undefined): AgentInputContextMode {
  switch (agentType) {
    case 'CodeStructureAnalyzer':
    case 'RemotionComponentTranslator':
    case 'DirectorAgent':
      return 'FullWorkflow';
    case 'DependencyAnalyzer':
    case 'ComponentInventoryAnalyzer':
    case 'RouteAndApiAnalyzer':
    case 'StyleAndThemeExtractor':
    case 'AnimationStrategyAgent':
    case 'ScriptwriterAgent':
    case 'AuthorAgent':
    case 'ReviewAgent':
      return 'PreviousStepOnly';
    default:
      return 'FullWorkflow';
  }
}
