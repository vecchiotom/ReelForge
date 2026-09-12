export interface WorkflowDefinition {
  id: string;
  name: string;
  createdAt: string;
  updatedAt: string;
  steps: WorkflowStep[];
  requiresUserInput: boolean;
}

export type StepType = 'Agent' | 'Conditional' | 'ForEach' | 'ReviewLoop' | 'Parallel' | 'Extract';
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
