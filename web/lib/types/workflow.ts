export interface WorkflowDefinition {
  id: string;
  name: string;
  createdAt: string;
  updatedAt: string;
  steps: WorkflowStep[];
  requiresUserInput: boolean;
}

export type StepType = 'Agent' | 'Conditional' | 'ForEach' | 'ReviewLoop' | 'Parallel';
export type StepStatus = 'Pending' | 'Running' | 'Completed' | 'Failed' | 'Skipped';

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
  trueBranchStepOrder?: string | null;
  falseBranchStepOrder?: string | null;
  /** JSON array of AgentDefinition GUIDs to run in parallel (Parallel step type only). */
  parallelAgentIdsJson?: string | null;
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
  trueBranchStepOrder?: string | null;
  falseBranchStepOrder?: string | null;
  /** JSON array of AgentDefinition GUIDs to run in parallel (Parallel step type only). */
  parallelAgentIdsJson?: string | null;
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
