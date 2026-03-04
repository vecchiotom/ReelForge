import { apiFetch } from './client';
import type { Project, CreateProjectRequest, UpdateProjectRequest } from '../types/project';

export function getProjects(): Promise<Project[]> {
  return apiFetch<Project[]>('/api/v1/projects');
}

export function getProject(id: string): Promise<Project> {
  return apiFetch<Project>(`/api/v1/projects/${id}`);
}

export function createProject(data: CreateProjectRequest): Promise<Project> {
  return apiFetch<Project>('/api/v1/projects', {
    method: 'POST',
    body: JSON.stringify(data),
  });
}

export function updateProject(id: string, data: UpdateProjectRequest): Promise<Project> {
  return apiFetch<Project>(`/api/v1/projects/${id}`, {
    method: 'PUT',
    body: JSON.stringify(data),
  });
}

export function deleteProject(id: string): Promise<void> {
  return apiFetch<void>(`/api/v1/projects/${id}`, { method: 'DELETE' });
}
