'use client';

import React from 'react';
import { Card, Group, Progress, Text, ThemeIcon, ActionIcon, Tooltip } from '@mantine/core';
import { IconCheck, IconX, IconTrash } from '@tabler/icons-react';
import type { Dispatch, SetStateAction } from 'react';

export interface UploadItem {
  id: string;
  file: File;
  progress: number;
  status: 'uploading' | 'done' | 'error';
  error?: string;
}

interface UploadProgressListProps {
  uploads: UploadItem[];
  setUploads: Dispatch<SetStateAction<UploadItem[]>>;
}

export function UploadProgressList({ uploads, setUploads }: UploadProgressListProps) {
  const handleRemove = (id: string) => {
    setUploads((u) => u.filter((x) => x.id !== id));
  };

  // automatically clear completed items after a short delay
  React.useEffect(() => {
    const timers: Array<NodeJS.Timeout> = [];
    uploads.forEach((u) => {
      if (u.status === 'done') {
        const t = setTimeout(() => handleRemove(u.id), 3000);
        timers.push(t);
      }
    });
    return () => {
      timers.forEach(clearTimeout);
    };
  }, [uploads]);

  return (
    <div style={{ marginTop: 16 }}>
      {uploads.map((u) => (
        <Card key={u.id} withBorder padding="sm" mb="sm">
          <Group justify="space-between" align="center">
            <Text size="sm" truncate style={{ maxWidth: 300 }}>
              {(u.file as any).webkitRelativePath || u.file.name}
            </Text>
            <Group gap="xs">
              {u.status === 'done' && (
                <ThemeIcon color="green" size="sm" variant="light">
                  <IconCheck size={14} />
                </ThemeIcon>
              )}
              {u.status === 'error' && (
                <Tooltip label={u.error || 'Upload error'}>
                  <ThemeIcon color="red" size="sm" variant="light">
                    <IconX size={14} />
                  </ThemeIcon>
                </Tooltip>
              )}
              <ActionIcon size="sm" onClick={() => handleRemove(u.id)}>
                <IconTrash size={16} />
              </ActionIcon>
            </Group>
          </Group>
          <Progress
            value={u.progress}
            mt="xs"
            size="sm"
            color={u.status === 'error' ? 'red' : u.status === 'done' ? 'green' : 'blue'}
          />
        </Card>
      ))}
    </div>
  );
}
