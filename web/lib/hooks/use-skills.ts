'use client';

import useSWR from 'swr';
import { fetcher } from '../api/client';
import type { Skill, SkillDetail } from '../types/skill';

// GET /api/v1/skills returns a bare JSON array (List<SkillSummaryResponse>), not a wrapper object.
export function useSkills() {
  return useSWR<Skill[]>('/api/v1/skills', fetcher);
}

export function useSkill(name: string) {
  return useSWR<SkillDetail>(name ? `/api/v1/skills/${encodeURIComponent(name)}` : null, fetcher);
}
