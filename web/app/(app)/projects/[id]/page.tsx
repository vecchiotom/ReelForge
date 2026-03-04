'use client';

import { use, useState } from 'react';
import { Tabs, Loader, Center, Text, Stack, Group, Button, ActionIcon } from '@mantine/core';
import { IconFiles, IconTopologyRing, IconInfoCircle, IconEdit, IconTrash } from '@tabler/icons-react';
import { useProject } from '@/lib/hooks/use-projects';
import { useProjectFiles } from '@/lib/hooks/use-files';
import { useWorkflows } from '@/lib/hooks/use-workflows';
import { deleteProject } from '@/lib/api/projects';
import { deleteFile } from '@/lib/api/files';
import { PageHeader } from '@/components/shared/PageHeader';
import { ProjectForm } from '@/components/projects/ProjectForm';
import { FileUploadZone } from '@/components/files/FileUploadZone';
import { FileList } from '@/components/files/FileList';
import { FileSummaryDrawer } from '@/components/files/FileSummaryDrawer';
import { ConfirmModal } from '@/components/shared/ConfirmModal';
import { EmptyState } from '@/components/shared/EmptyState';
import { formatDate } from '@/lib/utils/format';
import { notifications } from '@mantine/notifications';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import type { ProjectFile } from '@/lib/types/project';

export default function ProjectDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const { data: project, isLoading, mutate: mutateProject } = useProject(id);
  const { data: files, mutate: mutateFiles } = useProjectFiles(id);
  const { data: workflows } = useWorkflows(id);
  const router = useRouter();

  const [editOpened, setEditOpened] = useState(false);
  const [deleteOpened, setDeleteOpened] = useState(false);
  const [deleteLoading, setDeleteLoading] = useState(false);
  const [selectedFile, setSelectedFile] = useState<ProjectFile | null>(null);

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
        </Tabs.List>

        <Tabs.Panel value="overview">
          <Stack gap="sm">
            <Text size="sm" c="dimmed">{project.description || 'No description'}</Text>
            <Text size="xs" c="dimmed">Created {formatDate(project.createdAt)}</Text>
          </Stack>
        </Tabs.Panel>

        <Tabs.Panel value="files">
          <Stack gap="md">
            <FileUploadZone projectId={id} onSuccess={() => mutateFiles()} />
            {files && files.length > 0 ? (
              <FileList files={files} onDelete={handleDeleteFile} onSelect={setSelectedFile} />
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
                {workflows.map((wf) => (
                  <Button
                    key={wf.id}
                    component={Link}
                    href={`/projects/${id}/workflows/${wf.id}`}
                    variant="outline"
                    fullWidth
                    justify="space-between"
                    rightSection={<Text size="xs" c="dimmed">{wf.steps?.length || 0} steps</Text>}
                  >
                    {wf.name}
                  </Button>
                ))}
              </Stack>
            ) : (
              <EmptyState title="No workflows" description="Create a workflow to start generating videos." />
            )}
          </Stack>
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
      <FileSummaryDrawer file={selectedFile} opened={!!selectedFile} onClose={() => setSelectedFile(null)} />
    </>
  );
}
