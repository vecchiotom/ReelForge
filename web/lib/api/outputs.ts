import { apiFetch } from './client';

export interface OutputVideo {
  stepResultId: string;
  workflowExecutionId: string;
  workflowStepId: string;
  storageKey: string;
  fileName: string;
  producedAt: string;
}

export function listOutputVideos(projectId: string): Promise<OutputVideo[]> {
  return apiFetch<OutputVideo[]>(`/api/v1/projects/${projectId}/outputs`);
}

/** Returns the URL used to stream/download the output video inline. */
export function getOutputVideoUrl(projectId: string, stepResultId: string): string {
  return `/api/v1/projects/${projectId}/outputs/${stepResultId}/download`;
}
