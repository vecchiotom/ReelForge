import { apiFetch } from './client';
import type {
  ProjectFile,
  ProjectFileContent,
  MoveProjectFilesRequest,
  RenameFolderRequest,
} from '../types/project';

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
  options?: { relativePath?: string },
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
    const relativePath = options?.relativePath || (file as any).webkitRelativePath;
    if (relativePath) {
      formData.append('relativePath', relativePath);
    }
    xhr.send(formData);
  });
}

export function deleteFile(projectId: string, fileId: string): Promise<void> {
  return apiFetch<void>(`/api/v1/projects/${projectId}/files/${fileId}`, {
    method: 'DELETE',
  });
}

export function getProjectFileContent(projectId: string, fileId: string): Promise<ProjectFileContent> {
  return apiFetch<ProjectFileContent>(`/api/v1/projects/${projectId}/files/${fileId}/content`);
}

export function updateProjectFileContent(
  projectId: string,
  fileId: string,
  content: string,
  contentType?: string,
): Promise<ProjectFile> {
  return apiFetch<ProjectFile>(`/api/v1/projects/${projectId}/files/${fileId}/content`, {
    method: 'PUT',
    body: JSON.stringify({ content, contentType }),
  });
}

export function moveProjectFiles(projectId: string, payload: MoveProjectFilesRequest): Promise<ProjectFile[]> {
  return apiFetch<ProjectFile[]>(`/api/v1/projects/${projectId}/files/move`, {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export function createFolder(
  projectId: string,
  path: string,
): Promise<{ path: string; materialized: boolean; message: string }> {
  return apiFetch<{ path: string; materialized: boolean; message: string }>(`/api/v1/projects/${projectId}/files/folders`, {
    method: 'POST',
    body: JSON.stringify({ path }),
  });
}

export function renameFolder(
  projectId: string,
  payload: RenameFolderRequest,
): Promise<{ sourcePath: string; targetPath: string; movedFiles: number }> {
  return apiFetch<{ sourcePath: string; targetPath: string; movedFiles: number }>(`/api/v1/projects/${projectId}/files/folders`, {
    method: 'PATCH',
    body: JSON.stringify(payload),
  });
}

export function deleteFolder(
  projectId: string,
  path: string,
  recursive?: boolean,
): Promise<{ deletedFiles: number; path: string }> {
  return apiFetch<{ deletedFiles: number; path: string }>(`/api/v1/projects/${projectId}/files/folders`, {
    method: 'DELETE',
    body: JSON.stringify({ path, recursive }),
  });
}

export async function downloadFile(projectId: string, fileId: string): Promise<Blob> {
  const res = await fetch(`/api/v1/projects/${projectId}/files/${fileId}/download`);

  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error(body.message || 'Download failed');
  }

  return res.blob();
}
