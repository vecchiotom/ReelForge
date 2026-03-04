'use client';

import useSWR from 'swr';
import { fetcher } from '../api/client';
import type { AgentDefinition } from '../types/agent';

export function useAgents() {
  return useSWR<AgentDefinition[]>('/api/v1/agents', fetcher);
}

export function useAgent(id: string) {
  return useSWR<AgentDefinition>(id ? `/api/v1/agents/${id}` : null, fetcher);
}
