import { apiFetch } from './client';
import type {
  InferenceProvider,
  CreateInferenceProviderRequest,
  UpdateInferenceProviderRequest,
  TestInferenceProviderRequest,
  TestInferenceProviderResponse,
} from '../types/inference-provider';

export function getProviders(): Promise<InferenceProvider[]> {
  return apiFetch<InferenceProvider[]>('/api/v1/inference-providers');
}

export function getProvider(id: string): Promise<InferenceProvider> {
  return apiFetch<InferenceProvider>(`/api/v1/inference-providers/${id}`);
}

export function createProvider(data: CreateInferenceProviderRequest): Promise<InferenceProvider> {
  return apiFetch<InferenceProvider>('/api/v1/inference-providers', {
    method: 'POST',
    body: JSON.stringify(data),
  });
}

export function updateProvider(id: string, data: UpdateInferenceProviderRequest): Promise<InferenceProvider> {
  return apiFetch<InferenceProvider>(`/api/v1/inference-providers/${id}`, {
    method: 'PUT',
    body: JSON.stringify(data),
  });
}

export function deleteProvider(id: string): Promise<void> {
  return apiFetch<void>(`/api/v1/inference-providers/${id}`, { method: 'DELETE' });
}

/** Tests an already-saved provider by id. */
export function testProvider(id: string): Promise<TestInferenceProviderResponse> {
  return apiFetch<TestInferenceProviderResponse>(`/api/v1/inference-providers/${id}/test`, {
    method: 'POST',
  });
}

/** Tests an unsaved (or in-progress-edit) provider config before it is saved. */
export function testProviderConfig(data: TestInferenceProviderRequest): Promise<TestInferenceProviderResponse> {
  return apiFetch<TestInferenceProviderResponse>('/api/v1/inference-providers/test', {
    method: 'POST',
    body: JSON.stringify(data),
  });
}
