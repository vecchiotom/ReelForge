import { apiFetch } from './client';
import type { ProjectFile } from '../types/project';

export function getProjectFiles(projectId: string): Promise<ProjectFile[]> {
  return apiFetch<ProjectFile[]>(`/api/v1/projects/${projectId}/files`);
}

export async function uploadFile(projectId: string, file: File): Promise<ProjectFile> {
  const formData = new FormData();
  formData.append('file', file);

  const res = await fetch(`/api/v1/projects/${projectId}/files`, {
    method: 'POST',
    body: formData,
  });

  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error(body.message || 'Upload failed');
  }

  return res.json();
}

export function deleteFile(projectId: string, fileId: string): Promise<void> {
  return apiFetch<void>(`/api/v1/projects/${projectId}/files/${fileId}`, {
    method: 'DELETE',
  });
}
