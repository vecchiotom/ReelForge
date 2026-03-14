'use client';

import { use, useState } from 'react';
import { Tabs, Loader, Center, Text, Stack, Group, Button, ActionIcon, Card, TextInput, Checkbox } from '@mantine/core';
import { IconFiles, IconTopologyRing, IconInfoCircle, IconEdit, IconTrash, IconArrowRight, IconVideo } from '@tabler/icons-react';
import { useProject } from '@/lib/hooks/use-projects';
import { useProjectFiles } from '@/lib/hooks/use-files';
import { useWorkflows } from '@/lib/hooks/use-workflows';
import { deleteProject } from '@/lib/api/projects';
import {
  deleteFile,
  downloadFile,
  moveProjectFiles,
  createFolder,
  renameFolder,
  deleteFolder,
} from '@/lib/api/files';
import { PageHeader } from '@/components/shared/PageHeader';
import { ProjectForm } from '@/components/projects/ProjectForm';
import { FileUploadZone } from '@/components/files/FileUploadZone';
import { FileList } from '@/components/files/FileList';
import { FileSummaryDrawer } from '@/components/files/FileSummaryDrawer';
import { ConfirmModal } from '@/components/shared/ConfirmModal';
import { EmptyState } from '@/components/shared/EmptyState';
import { formatDate } from '@/lib/utils/format';
import { StepTypeBadge } from '@/components/workflows/StepTypeBadge';
import type { StepType } from '@/lib/types/workflow';
import { notifications } from '@mantine/notifications';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import type { ProjectFile } from '@/lib/types/project';
import { useOutputVideos } from '@/lib/hooks/use-outputs';
import { getOutputVideoUrl } from '@/lib/api/outputs';

export default function ProjectDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const { data: project, isLoading, mutate: mutateProject } = useProject(id);
  const { data: files, mutate: mutateFiles } = useProjectFiles(id);
  const { data: workflows } = useWorkflows(id);
  const { data: outputs } = useOutputVideos(id);
  const router = useRouter();

  const [editOpened, setEditOpened] = useState(false);
  const [deleteOpened, setDeleteOpened] = useState(false);
  const [deleteLoading, setDeleteLoading] = useState(false);
  const [selectedFile, setSelectedFile] = useState<ProjectFile | null>(null);
  const [uploadPath, setUploadPath] = useState('');
  const [newFolderPath, setNewFolderPath] = useState('');
  const [renameSourcePath, setRenameSourcePath] = useState('');
  const [renameTargetPath, setRenameTargetPath] = useState('');
  const [deleteFolderPath, setDeleteFolderPath] = useState('');
  const [deleteFolderRecursive, setDeleteFolderRecursive] = useState(false);

  if (isLoading) return <Center h={300}><Loader /></Center>;
  if (!project) return <Text>Project not found</Text>;

  const handleDeleteProject = async () => {
    setDeleteLoading(true);
    try {
      await deleteProject(id);
      notifications.show({ title: 'Deleted', message: 'Project deleted', color: 'green' });
      router.push('/projects');
    } catch {
      notifications.show({ title: 'Error', message: 'Failed to delete project', color: 'red' });
    } finally {
      setDeleteLoading(false);
    }
  };

  const handleDeleteFile = async (fileId: string) => {
    try {
      await deleteFile(id, fileId);
      mutateFiles();
      notifications.show({ title: 'Deleted', message: 'File deleted', color: 'green' });
    } catch {
      notifications.show({ title: 'Error', message: 'Failed to delete file', color: 'red' });
    }
  };

  const handleDownloadFile = async (file: ProjectFile) => {
    try {
      const blob = await downloadFile(id, file.id);
      const objectUrl = URL.createObjectURL(blob);
      const anchor = document.createElement('a');
      anchor.href = objectUrl;
      anchor.download = file.originalFileName;
      document.body.appendChild(anchor);
      anchor.click();
      document.body.removeChild(anchor);
      URL.revokeObjectURL(objectUrl);
      notifications.show({ title: 'Downloaded', message: 'File download started', color: 'green' });
    } catch {
      notifications.show({ title: 'Error', message: 'Failed to download file', color: 'red' });
    }
  };

  const handleMoveSingleFile = async (file: ProjectFile) => {
    const promptValue = window.prompt('Target directory path', file.directoryPath || '');
    if (promptValue === null) return;

    try {
      await moveProjectFiles(id, {
        fileIds: [file.id],
        targetDirectoryPath: promptValue || undefined,
      });
      await mutateFiles();
      notifications.show({ title: 'Moved', message: 'File moved', color: 'green' });
    } catch {
      notifications.show({ title: 'Error', message: 'Failed to move file', color: 'red' });
    }
  };

  const handleCreateFolder = async () => {
    if (!newFolderPath.trim()) {
      notifications.show({ title: 'Error', message: 'Folder path is required', color: 'red' });
      return;
    }

    try {
      await createFolder(id, newFolderPath.trim());
      await mutateFiles();
      notifications.show({ title: 'Created', message: 'Folder created', color: 'green' });
    } catch {
      notifications.show({ title: 'Error', message: 'Failed to create folder', color: 'red' });
    }
  };

  const handleRenameFolder = async () => {
    if (!renameSourcePath.trim() || !renameTargetPath.trim()) {
      notifications.show({ title: 'Error', message: 'Source and target paths are required', color: 'red' });
      return;
    }

    try {
      await renameFolder(id, { sourcePath: renameSourcePath.trim(), targetPath: renameTargetPath.trim() });
      await mutateFiles();
      notifications.show({ title: 'Renamed', message: 'Folder renamed', color: 'green' });
    } catch {
      notifications.show({ title: 'Error', message: 'Failed to rename folder', color: 'red' });
    }
  };

  const handleDeleteFolder = async () => {
    if (!deleteFolderPath.trim()) {
      notifications.show({ title: 'Error', message: 'Folder path is required', color: 'red' });
      return;
    }

    try {
      await deleteFolder(id, deleteFolderPath.trim(), deleteFolderRecursive);
      await mutateFiles();
      notifications.show({ title: 'Deleted', message: 'Folder deleted', color: 'green' });
    } catch {
      notifications.show({ title: 'Error', message: 'Failed to delete folder', color: 'red' });
    }
  };

  return (
    <>
      <PageHeader
        title={project.name}
        breadcrumbs={[{ label: 'Projects', href: '/projects' }, { label: project.name }]}
      >
        <ActionIcon variant="default" size="lg" onClick={() => setEditOpened(true)}>
          <IconEdit size={16} />
        </ActionIcon>
        <ActionIcon variant="default" size="lg" color="red" onClick={() => setDeleteOpened(true)}>
          <IconTrash size={16} />
        </ActionIcon>
      </PageHeader>

      <Tabs defaultValue="overview">
        <Tabs.List mb="md">
          <Tabs.Tab value="overview" leftSection={<IconInfoCircle size={16} />}>Overview</Tabs.Tab>
          <Tabs.Tab value="files" leftSection={<IconFiles size={16} />}>Files ({files?.length || 0})</Tabs.Tab>
          <Tabs.Tab value="workflows" leftSection={<IconTopologyRing size={16} />}>Workflows ({workflows?.length || 0})</Tabs.Tab>
          <Tabs.Tab value="outputs" leftSection={<IconVideo size={16} />}>Outputs ({outputs?.length || 0})</Tabs.Tab>
        </Tabs.List>

        <Tabs.Panel value="overview">
          <Stack gap="sm">
            <Text size="sm" c="dimmed">{project.description || 'No description'}</Text>
            <Text size="xs" c="dimmed">Created {formatDate(project.createdAt)}</Text>
          </Stack>
        </Tabs.Panel>

        <Tabs.Panel value="files">
          <Stack gap="md">
            <TextInput
              label="Upload path"
              placeholder="Optional base directory, e.g. docs/source"
              value={uploadPath}
              onChange={(event) => setUploadPath(event.currentTarget.value)}
            />
            <Group align="end">
              <TextInput
                label="Create folder"
                placeholder="Folder path"
                value={newFolderPath}
                onChange={(event) => setNewFolderPath(event.currentTarget.value)}
                style={{ flex: 1 }}
              />
              <Button onClick={handleCreateFolder}>Create</Button>
            </Group>
            <Group align="end">
              <TextInput
                label="Rename folder (source)"
                placeholder="Old path"
                value={renameSourcePath}
                onChange={(event) => setRenameSourcePath(event.currentTarget.value)}
                style={{ flex: 1 }}
              />
              <TextInput
                label="Rename folder (target)"
                placeholder="New path"
                value={renameTargetPath}
                onChange={(event) => setRenameTargetPath(event.currentTarget.value)}
                style={{ flex: 1 }}
              />
              <Button onClick={handleRenameFolder}>Rename</Button>
            </Group>
            <Group align="end">
              <TextInput
                label="Delete folder"
                placeholder="Folder path"
                value={deleteFolderPath}
                onChange={(event) => setDeleteFolderPath(event.currentTarget.value)}
                style={{ flex: 1 }}
              />
              <Checkbox
                label="Recursive"
                checked={deleteFolderRecursive}
                onChange={(event) => setDeleteFolderRecursive(event.currentTarget.checked)}
                mb={8}
              />
              <Button color="red" variant="light" onClick={handleDeleteFolder}>Delete</Button>
            </Group>
            <FileUploadZone projectId={id} targetDirectoryPath={uploadPath} onSuccess={() => mutateFiles()} />
            {files && files.length > 0 ? (
              <FileList
                files={files}
                onDelete={handleDeleteFile}
                onSelect={setSelectedFile}
                onDownload={handleDownloadFile}
                onMove={handleMoveSingleFile}
              />
            ) : (
              <EmptyState title="No files" description="Upload files to analyze in your workflows." />
            )}
          </Stack>
        </Tabs.Panel>

        <Tabs.Panel value="workflows">
          <Stack gap="md">
            <Group>
              <Button component={Link} href={`/projects/${id}/workflows/new`}>
                New Workflow
              </Button>
            </Group>
            {workflows && workflows.length > 0 ? (
              <Stack gap="sm">
                {workflows.map((wf) => {
                  const typeCounts: Partial<Record<StepType, number>> = {};
                  for (const step of wf.steps || []) {
                    const t = (step.stepType || 'Agent') as StepType;
                    typeCounts[t] = (typeCounts[t] || 0) + 1;
                  }
                  return (
                    <Card key={wf.id} component={Link} href={`/projects/${id}/workflows/${wf.id}`} withBorder padding="sm" radius="md" style={{ textDecoration: 'none' }}>
                      <Group justify="space-between" wrap="nowrap">
                        <Stack gap={4}>
                          <Text fw={500} size="sm">{wf.name}</Text>
                          <Group gap={4}>
                            <Text size="xs" c="dimmed">{wf.steps?.length || 0} steps</Text>
                            {(Object.entries(typeCounts) as [StepType, number][]).map(([type, count]) => (
                              <Group gap={2} key={type}>
                                <StepTypeBadge stepType={type} />
                                {count > 1 && <Text size="xs" c="dimmed">x{count}</Text>}
                              </Group>
                            ))}
                          </Group>
                        </Stack>
                        <IconArrowRight size={16} style={{ opacity: 0.5 }} />
                      </Group>
                    </Card>
                  );
                })}
              </Stack>
            ) : (
              <EmptyState title="No workflows" description="Create a workflow to start generating videos." />
            )}
          </Stack>
        </Tabs.Panel>

        <Tabs.Panel value="outputs">
          {outputs && outputs.length > 0 ? (
            <Stack gap="md">
              {outputs.map((output) => (
                <Card key={output.stepResultId} withBorder padding="md" radius="md">
                  <Stack gap="sm">
                    <Group justify="space-between">
                      <Text fw={500} size="sm">{output.fileName}</Text>
                      <Text size="xs" c="dimmed">{formatDate(output.producedAt)}</Text>
                    </Group>
                    <video
                      controls
                      style={{ width: '100%', borderRadius: 8 }}
                      src={getOutputVideoUrl(id, output.stepResultId)}
                    />
                  </Stack>
                </Card>
              ))}
            </Stack>
          ) : (
            <EmptyState title="No outputs" description="Run a workflow to generate output videos." />
          )}
        </Tabs.Panel>
      </Tabs>

      <ProjectForm opened={editOpened} onClose={() => setEditOpened(false)} onSuccess={() => mutateProject()} project={project} />
      <ConfirmModal
        opened={deleteOpened}
        onClose={() => setDeleteOpened(false)}
        onConfirm={handleDeleteProject}
        title="Delete Project"
        message="This will permanently delete this project and all its files and workflows."
        loading={deleteLoading}
      />
      <FileSummaryDrawer
        projectId={id}
        file={selectedFile}
        opened={!!selectedFile}
        onClose={() => setSelectedFile(null)}
        onSaved={() => mutateFiles()}
        onDownload={handleDownloadFile}
      />
    </>
  );
}
