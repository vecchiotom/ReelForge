import { apiFetch } from './client';
import type {
  WorkflowDefinition,
  CreateWorkflowRequest,
  UpdateWorkflowRequest,
  WorkflowExecution,
} from '../types/workflow';

export function getWorkflows(projectId: string): Promise<WorkflowDefinition[]> {
  return apiFetch<WorkflowDefinition[]>(`/api/v1/projects/${projectId}/workflows`);
}

export function getWorkflow(projectId: string, workflowId: string): Promise<WorkflowDefinition> {
  return apiFetch<WorkflowDefinition>(`/api/v1/projects/${projectId}/workflows/${workflowId}`);
}

export function createWorkflow(projectId: string, data: CreateWorkflowRequest): Promise<WorkflowDefinition> {
  return apiFetch<WorkflowDefinition>(`/api/v1/projects/${projectId}/workflows`, {
    method: 'POST',
    body: JSON.stringify(data),
  });
}

export function updateWorkflow(
  projectId: string,
  workflowId: string,
  data: UpdateWorkflowRequest,
): Promise<WorkflowDefinition> {
  return apiFetch<WorkflowDefinition>(`/api/v1/projects/${projectId}/workflows/${workflowId}`, {
    method: 'PUT',
    body: JSON.stringify(data),
  });
}

export function deleteWorkflow(projectId: string, workflowId: string): Promise<void> {
  return apiFetch<void>(`/api/v1/projects/${projectId}/workflows/${workflowId}`, {
    method: 'DELETE',
  });
}

export function executeWorkflow(projectId: string, workflowId: string): Promise<WorkflowExecution> {
  return apiFetch<WorkflowExecution>(`/api/v1/projects/${projectId}/workflows/${workflowId}/execute`, {
    method: 'POST',
  });
}

export function getWorkflowExecutions(projectId: string, workflowId: string): Promise<WorkflowExecution[]> {
  return apiFetch<WorkflowExecution[]>(`/api/v1/projects/${projectId}/workflows/${workflowId}/executions`);
}
