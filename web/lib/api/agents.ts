import { apiFetch } from './client';
import type { AgentDefinition, CreateAgentRequest, UpdateAgentRequest } from '../types/agent';

export function getAgents(): Promise<AgentDefinition[]> {
  return apiFetch<AgentDefinition[]>('/api/v1/agents');
}

export function getAgent(id: string): Promise<AgentDefinition> {
  return apiFetch<AgentDefinition>(`/api/v1/agents/${id}`);
}

export function createAgent(data: CreateAgentRequest): Promise<AgentDefinition> {
  return apiFetch<AgentDefinition>('/api/v1/agents', {
    method: 'POST',
    body: JSON.stringify(data),
  });
}

export function updateAgent(id: string, data: UpdateAgentRequest): Promise<AgentDefinition> {
  return apiFetch<AgentDefinition>(`/api/v1/agents/${id}`, {
    method: 'PUT',
    body: JSON.stringify(data),
  });
}

export function deleteAgent(id: string): Promise<void> {
  return apiFetch<void>(`/api/v1/agents/${id}`, { method: 'DELETE' });
}
