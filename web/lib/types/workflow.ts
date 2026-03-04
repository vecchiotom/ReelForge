export interface WorkflowDefinition {
  id: string;
  projectId: string;
  name: string;
  description: string;
  createdAt: string;
  updatedAt: string;
  steps: WorkflowStep[];
}

export interface WorkflowStep {
  id: string;
  workflowDefinitionId: string;
  agentDefinitionId: string;
  agentDefinition?: import('./agent').AgentDefinition;
  stepOrder: number;
  label: string;
}

export interface CreateWorkflowRequest {
  name: string;
  description: string;
  steps: CreateWorkflowStepRequest[];
}

export interface CreateWorkflowStepRequest {
  agentDefinitionId: string;
  stepOrder: number;
  label: string;
}

export interface UpdateWorkflowRequest {
  name?: string;
  description?: string;
  steps?: CreateWorkflowStepRequest[];
}

export interface WorkflowExecution {
  id: string;
  workflowDefinitionId: string;
  status: 'Queued' | 'Running' | 'Passed' | 'Failed';
  startedAt: string | null;
  completedAt: string | null;
  createdAt: string;
  stepResults: WorkflowStepResult[];
  reviewScores: ReviewScore[];
}

export interface WorkflowStepResult {
  id: string;
  workflowExecutionId: string;
  workflowStepId: string;
  agentDefinitionId: string;
  status: 'Pending' | 'Running' | 'Completed' | 'Failed';
  output: string | null;
  tokensUsed: number;
  durationMs: number;
  createdAt: string;
  completedAt: string | null;
}

export interface ReviewScore {
  id: string;
  workflowExecutionId: string;
  iteration: number;
  score: number;
  feedback: string;
  createdAt: string;
}
