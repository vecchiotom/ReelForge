'use client';

import useSWR from 'swr';
import { fetcher } from '../api/client';
import type { WorkflowDefinition } from '../types/workflow';

export function useWorkflows(projectId: string) {
  return useSWR<WorkflowDefinition[]>(
    projectId ? `/api/v1/projects/${projectId}/workflows` : null,
    fetcher,
  );
}

export function useWorkflow(projectId: string, workflowId: string) {
  return useSWR<WorkflowDefinition>(
    projectId && workflowId
      ? `/api/v1/projects/${projectId}/workflows/${workflowId}`
      : null,
    fetcher,
  );
}
