import { apiFetch } from './client';

export interface WorkflowEngineStatus {
  service: string;
  status: string;
  timestamp: string;
}

export function getWorkflowEngineHealth(): Promise<WorkflowEngineStatus> {
  return apiFetch<WorkflowEngineStatus>('/api/v1/workflow-engine/health');
}

export function getWorkflowEngineAdminStatus(): Promise<WorkflowEngineStatus> {
  return apiFetch<WorkflowEngineStatus>('/api/v1/workflow-engine/admin/status');
}
