import { apiFetch } from './client';
import type { Skill, SkillDetail } from '../types/skill';
import type { AgentDefinition } from '../types/agent';

export function listSkills(): Promise<{ skills: Skill[] }> {
  return apiFetch<{ skills: Skill[] }>('/api/v1/skills');
}

export function getSkill(name: string): Promise<SkillDetail> {
  return apiFetch<SkillDetail>(`/api/v1/skills/${encodeURIComponent(name)}`);
}

export function setAgentSkills(agentId: string, skills: string[]): Promise<AgentDefinition> {
  return apiFetch<AgentDefinition>(`/api/v1/agents/${agentId}/skills`, {
    method: 'PUT',
    body: JSON.stringify({ skills }),
  });
}
