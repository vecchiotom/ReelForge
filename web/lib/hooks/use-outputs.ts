'use client';

import useSWR from 'swr';
import { fetcher } from '../api/client';
import type { OutputVideo } from '../api/outputs';

export function useOutputVideos(projectId: string) {
  return useSWR<OutputVideo[]>(
    projectId ? `/api/v1/projects/${projectId}/outputs` : null,
    fetcher,
  );
}
