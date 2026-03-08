import { apiFetch } from './client';
import type { WorkflowExecution } from '../types/workflow';

export function getExecution(
  projectId: string,
  executionId: string,
): Promise<WorkflowExecution> {
  // workflowId is not required; the inference API exposes a root-level endpoint
  // that does not include the workflow segment.
  return apiFetch<WorkflowExecution>(
    `/api/v1/projects/${projectId}/executions/${executionId}`,
  );
}

export function stopExecution(
  projectId: string,
  workflowId: string,
  executionId: string,
): Promise<void> {
  // stopping an execution is now handled by the Inference API directly,
  // so there is no longer any special nginx proxying hack.  we keep the
  // same URL format as before for compatibility with existing clients.
  return apiFetch<void>(
    `/api/v1/projects/${projectId}/workflows/${workflowId}/executions/${executionId}/stop`,
    { method: 'POST' },
  );
}
