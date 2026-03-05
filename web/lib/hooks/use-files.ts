'use client';

import useSWR from 'swr';
import { fetcher } from '../api/client';
import type { ProjectFile } from '../types/project';

function needsPolling(files: ProjectFile[] | undefined | null) {
  if (!files) return false;
  return files.some(
    (f) => f.summaryStatus === 'Pending' || f.summaryStatus === 'Processing',
  );
}

export function useProjectFiles(projectId: string) {
  return useSWR<ProjectFile[]>(
    projectId ? `/api/v1/projects/${projectId}/files` : null,
    fetcher,
    {
      refreshInterval: (data) => (needsPolling(data) ? 10000 : 0),
    },
  );
}
