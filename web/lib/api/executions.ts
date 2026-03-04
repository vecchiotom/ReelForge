import { apiFetch } from './client';
import type { WorkflowExecution } from '../types/workflow';

export function getExecution(
  projectId: string,
  workflowId: string,
  executionId: string,
): Promise<WorkflowExecution> {
  return apiFetch<WorkflowExecution>(
    `/api/v1/projects/${projectId}/workflows/${workflowId}/executions/${executionId}`,
  );
}
