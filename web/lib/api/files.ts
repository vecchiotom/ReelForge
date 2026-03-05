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

// similar to uploadFile but provides progress updates via callback
export function uploadFileWithProgress(
  projectId: string,
  file: File,
  onProgress: (percent: number) => void,
): Promise<ProjectFile> {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open('POST', `/api/v1/projects/${projectId}/files`);

    xhr.upload.onprogress = (event) => {
      if (event.lengthComputable) {
        const percent = Math.round((event.loaded / event.total) * 100);
        onProgress(percent);
      }
    };

    xhr.onreadystatechange = () => {
      if (xhr.readyState === XMLHttpRequest.DONE) {
        if (xhr.status >= 200 && xhr.status < 300) {
          try {
            const json = JSON.parse(xhr.responseText);
            resolve(json);
          } catch (err) {
            reject(err);
          }
        } else {
          let message = 'Upload failed';
          try {
            const json = JSON.parse(xhr.responseText);
            message = json.message || message;
          } catch {}
          reject(new Error(message));
        }
      }
    };

    xhr.onerror = () => {
      reject(new Error('Network error'));
    };

    const formData = new FormData();
    formData.append('file', file);
    xhr.send(formData);
  });
}

export function deleteFile(projectId: string, fileId: string): Promise<void> {
  return apiFetch<void>(`/api/v1/projects/${projectId}/files/${fileId}`, {
    method: 'DELETE',
  });
}
