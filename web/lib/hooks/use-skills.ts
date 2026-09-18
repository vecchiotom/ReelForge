'use client';

import useSWR from 'swr';
import { fetcher } from '../api/client';
import type { Skill, SkillDetail } from '../types/skill';

export function useSkills() {
  return useSWR<{ skills: Skill[] }>('/api/v1/skills', fetcher);
}

export function useSkill(name: string) {
  return useSWR<SkillDetail>(name ? `/api/v1/skills/${encodeURIComponent(name)}` : null, fetcher);
}
