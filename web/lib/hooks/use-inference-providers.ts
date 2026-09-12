'use client';

import useSWR from 'swr';
import { fetcher } from '../api/client';
import type { InferenceProvider } from '../types/inference-provider';

export function useInferenceProviders() {
  return useSWR<InferenceProvider[]>('/api/v1/inference-providers', fetcher);
}

export function useInferenceProvider(id: string) {
  return useSWR<InferenceProvider>(id ? `/api/v1/inference-providers/${id}` : null, fetcher);
}
