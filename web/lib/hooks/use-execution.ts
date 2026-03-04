'use client';

import useSWR from 'swr';
import { fetcher } from '../api/client';
import type { WorkflowExecution } from '../types/workflow';

export function useExecution(
  projectId: string,
  workflowId: string,
  executionId: string,
) {
  const isTerminal = (data: WorkflowExecution | undefined) =>
    data?.status === 'Passed' || data?.status === 'Failed';

  const { data, ...rest } = useSWR<WorkflowExecution>(
    projectId && workflowId && executionId
      ? `/api/v1/projects/${projectId}/workflows/${workflowId}/executions/${executionId}`
      : null,
    fetcher,
    {
      refreshInterval: (latestData) => (isTerminal(latestData) ? 0 : 2000),
    },
  );

  return { data, ...rest };
}
