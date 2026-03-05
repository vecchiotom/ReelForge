'use client';

import useSWR from 'swr';
import { fetcher } from '../api/client';
import type { WorkflowEngineStatus } from '../api/workflow-engine';

export function useWorkflowEngineStatus() {
  return useSWR<WorkflowEngineStatus>(
    '/api/v1/workflow-engine/admin/status',
    fetcher,
    { refreshInterval: 30000 },
  );
}
