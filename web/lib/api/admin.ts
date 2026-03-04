import { apiFetch } from './client';
import type { AdminUser, CreateUserRequest, CreateUserResponse, UpdateUserRequest } from '../types/admin';

export function getUsers(): Promise<AdminUser[]> {
  return apiFetch<AdminUser[]>('/api/v1/admin/users');
}

export function getUser(id: string): Promise<AdminUser> {
  return apiFetch<AdminUser>(`/api/v1/admin/users/${id}`);
}

export function createUser(data: CreateUserRequest): Promise<CreateUserResponse> {
  return apiFetch<CreateUserResponse>('/api/v1/admin/users', {
    method: 'POST',
    body: JSON.stringify(data),
  });
}

export function updateUser(id: string, data: UpdateUserRequest): Promise<AdminUser> {
  return apiFetch<AdminUser>(`/api/v1/admin/users/${id}`, {
    method: 'PUT',
    body: JSON.stringify(data),
  });
}

export function deleteUser(id: string): Promise<void> {
  return apiFetch<void>(`/api/v1/admin/users/${id}`, { method: 'DELETE' });
}
