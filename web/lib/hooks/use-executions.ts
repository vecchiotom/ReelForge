'use client';

import useSWR from 'swr';
import { fetcher } from '../api/client';
import type { WorkflowExecution } from '../types/workflow';

export function useExecutions(projectId: string, workflowId: string) {
  return useSWR<WorkflowExecution[]>(
    projectId && workflowId
      ? `/api/v1/projects/${projectId}/workflows/${workflowId}/executions`
      : null,
    fetcher,
  );
}
