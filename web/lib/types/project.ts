export interface Project {
  id: string;
  name: string;
  description: string;
  status: string;
  userId: string;
  createdAt: string;
  updatedAt: string;
}

export interface CreateProjectRequest {
  name: string;
  description: string;
}

export interface UpdateProjectRequest {
  name?: string;
  description?: string;
}

export interface ProjectFile {
  id: string;
  projectId: string;
  fileName: string;
  fileSize: number;
  contentType: string;
  storagePath: string;
  summary: string | null;
  summaryStatus: 'Pending' | 'Processing' | 'Completed' | 'Failed';
  createdAt: string;
}
