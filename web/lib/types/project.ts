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
  originalFileName: string;
  mimeType: string;
  sizeBytes: number;
  storageKey: string;
  agentSummary: string | null;
  summaryStatus: 'Pending' | 'Processing' | 'Completed' | 'Failed';
  uploadedAt: string;
}
