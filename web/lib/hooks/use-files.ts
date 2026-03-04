'use client';

import useSWR from 'swr';
import { fetcher } from '../api/client';
import type { ProjectFile } from '../types/project';

export function useProjectFiles(projectId: string) {
  return useSWR<ProjectFile[]>(
    projectId ? `/api/v1/projects/${projectId}/files` : null,
    fetcher,
  );
}
