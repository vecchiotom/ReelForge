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
  originalPath?: string; // may include folder segments when imported from directories
  directoryPath?: string;
  category: string; // userFiles | agentFiles | outputFiles
  mimeType: string;
  sizeBytes: number;
  storageKey: string;
  storageFileName?: string;
  agentSummary: string | null;
  summaryStatus: 'Pending' | 'Processing' | 'Completed' | 'Failed';
  uploadedAt: string;
}

export interface ProjectFileContent {
  id: string;
  originalFileName: string;
  originalPath?: string;
  mimeType: string;
  content: string;
  uploadedAt: string;
}

export interface MoveProjectFilesRequest {
  fileIds: string[];
  targetDirectoryPath?: string;
  targetCategory?: 'userFiles' | 'agentFiles' | 'outputFiles';
}

export interface RenameFolderRequest {
  sourcePath: string;
  targetPath: string;
}

export interface DeleteFolderRequest {
  path: string;
  recursive?: boolean;
}
