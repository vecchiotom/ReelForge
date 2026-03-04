'use client';

import useSWR from 'swr';
import { fetcher } from '../api/client';
import type { AdminUser } from '../types/admin';

export function useUsers() {
  return useSWR<AdminUser[]>('/api/v1/admin/users', fetcher);
}

export function useUser(id: string) {
  return useSWR<AdminUser>(id ? `/api/v1/admin/users/${id}` : null, fetcher);
}
