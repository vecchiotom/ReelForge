import { apiFetch } from './client';
import type { WorkflowExecution } from '../types/workflow';

export function getExecution(
  projectId: string,
  executionId: string,
): Promise<WorkflowExecution> {
  // workflowId is not required; the inference API exposes a root-level endpoint
  // that does not include the workflow segment.
  return apiFetch<WorkflowExecution>(
    `/api/v1/projects/${projectId}/executions/${executionId}`,
  );
}

export function stopExecution(
  projectId: string,
  workflowId: string,
  executionId: string,
): Promise<void> {
  // the POST route for stopping an execution is implemented in the
  // **Go API** (permission checks + rabbitmq publish), but the frontend
  // normally talks to the Inference service via nginx.  nginx is
  // configured to proxy only URLs beginning with `/api/v1/workflows/`
  // to the Go API, so we must use the full project-prefixed path here
  // and nginx will reroute the request with a dedicated location regex
  // (see `nginx/nginx.conf`) otherwise we'd hit the inference server and
  // receive a 404.  This mirrors the path registered in
  // `api/handlers/handlers.go`.
  return apiFetch<void>(
    `/api/v1/projects/${projectId}/workflows/${workflowId}/executions/${executionId}/stop`,
    { method: 'POST' },
  );
}
