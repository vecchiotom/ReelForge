'use client';

import useSWR from 'swr';
import { fetcher } from '../api/client';
import type { Project } from '../types/project';

export function useProjects() {
  return useSWR<Project[]>('/api/v1/projects', fetcher);
}

export function useProject(id: string) {
  return useSWR<Project>(id ? `/api/v1/projects/${id}` : null, fetcher);
}
