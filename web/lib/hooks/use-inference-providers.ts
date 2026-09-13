'use client';

import useSWR from 'swr';
import { fetcher } from '../api/client';
import type { InferenceProvider } from '../types/inference-provider';

export function useInferenceProviders(enabled: boolean = true) {
  return useSWR<InferenceProvider[]>(enabled ? '/api/v1/inference-providers' : null, fetcher);
}

export function useInferenceProvider(id: string) {
  return useSWR<InferenceProvider>(id ? `/api/v1/inference-providers/${id}` : null, fetcher);
}
