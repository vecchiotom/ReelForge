import { apiFetch } from './client';

/**
 * Fetches a step result's large artifact (e.g. a VideoAnalyze full analysis JSON or a
 * VideoCompile EDL) — the counterpart to `WorkflowStepResult.artifactStorageKey`, kept separate
 * from `outputStorageKey`/`getOutputVideoUrl` so the execution UI never tries to play JSON as a
 * video. Backed by `GET /api/v1/projects/{projectId}/step-results/{stepResultId}/artifact`.
 */
export function getStepResultArtifact(projectId: string, stepResultId: string): Promise<unknown> {
  return apiFetch<unknown>(`/api/v1/projects/${projectId}/step-results/${stepResultId}/artifact`);
}
