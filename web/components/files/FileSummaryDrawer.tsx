'use client';

import { useEffect, useState } from 'react';
import { Drawer, Text, Stack, Badge, Code, Textarea, Group, Button } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { StatusBadge } from '@/components/projects/StatusBadge';
import { getProjectFileContent, updateProjectFileContent } from '@/lib/api/files';
import type { ProjectFile } from '@/lib/types/project';

interface FileSummaryDrawerProps {
  projectId: string;
  file: ProjectFile | null;
  opened: boolean;
  onClose: () => void;
  onSaved?: () => void;
  onDownload?: (file: ProjectFile) => void;
}

export function FileSummaryDrawer({ projectId, file, opened, onClose, onSaved, onDownload }: FileSummaryDrawerProps) {
  const [content, setContent] = useState('');
  const [canSave, setCanSave] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [isLoadingContent, setIsLoadingContent] = useState(false);
  const [isSaving, setIsSaving] = useState(false);

  useEffect(() => {
    if (!opened || !file) return;

    setIsLoadingContent(true);
    setLoadError(null);
    setCanSave(false);
    setContent('');

    getProjectFileContent(projectId, file.id)
      .then((result) => {
        setContent(result.content ?? '');
        setCanSave(true);
      })
      .catch(() => {
        setLoadError('File content is not available for inline editing. This is likely a non-text file.');
        setCanSave(false);
      })
      .finally(() => {
        setIsLoadingContent(false);
      });
  }, [opened, file, projectId]);

  if (!file) return null;

  const handleSave = async () => {
    if (!canSave) return;
    setIsSaving(true);
    try {
      await updateProjectFileContent(projectId, file.id, content);
      notifications.show({ title: 'Saved', message: 'File content updated', color: 'green' });
      onSaved?.();
    } catch {
      notifications.show({ title: 'Error', message: 'Failed to save file content', color: 'red' });
    } finally {
      setIsSaving(false);
    }
  };

  const title = file.originalPath ? file.originalPath : file.originalFileName;
  return (
    <Drawer opened={opened} onClose={onClose} title={title} position="right" size="lg">
      <Stack gap="md">
        <Group justify="flex-end">
          {onDownload ? (
            <Button variant="light" onClick={() => onDownload(file)}>
              Download
            </Button>
          ) : null}
        </Group>
        <div>
          <Text size="sm" fw={500}>Content Type</Text>
          <Badge variant="light">{file.mimeType}</Badge>
        </div>
        <div>
          <Text size="sm" fw={500}>Summary Status</Text>
          <StatusBadge status={file.summaryStatus} />
        </div>
        <div>
          <Text size="sm" fw={500}>Summary</Text>
          {file.agentSummary ? (
            <Code block>{file.agentSummary}</Code>
          ) : (
            <Text size="sm" c="dimmed">No summary available yet</Text>
          )}
        </div>
        <div>
          <Text size="sm" fw={500}>Content</Text>
          {isLoadingContent ? (
            <Text size="sm" c="dimmed">Loading content...</Text>
          ) : loadError ? (
            <Text size="sm" c="dimmed">{loadError}</Text>
          ) : (
            <Stack gap="sm">
              <Textarea
                value={content}
                onChange={(event) => setContent(event.currentTarget.value)}
                minRows={12}
                autosize
              />
              <Group justify="flex-end">
                <Button onClick={handleSave} loading={isSaving} disabled={!canSave}>
                  Save
                </Button>
              </Group>
            </Stack>
          )}
        </div>
      </Stack>
    </Drawer>
  );
}
