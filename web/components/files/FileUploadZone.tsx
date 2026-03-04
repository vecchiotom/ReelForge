'use client';

import { useState } from 'react';
import { Text, Group } from '@mantine/core';
import { Dropzone } from '@mantine/dropzone';
import { IconUpload, IconX, IconFile } from '@tabler/icons-react';
import { notifications } from '@mantine/notifications';
import { uploadFile } from '@/lib/api/files';

interface FileUploadZoneProps {
  projectId: string;
  onSuccess: () => void;
}

export function FileUploadZone({ projectId, onSuccess }: FileUploadZoneProps) {
  const [loading, setLoading] = useState(false);

  const handleDrop = async (files: File[]) => {
    setLoading(true);
    try {
      for (const file of files) {
        await uploadFile(projectId, file);
      }
      notifications.show({ title: 'Uploaded', message: `${files.length} file(s) uploaded`, color: 'green' });
      onSuccess();
    } catch (err: unknown) {
      notifications.show({
        title: 'Upload failed',
        message: err instanceof Error ? err.message : 'Upload failed',
        color: 'red',
      });
    } finally {
      setLoading(false);
    }
  };

  return (
    <Dropzone onDrop={handleDrop} loading={loading} maxSize={100 * 1024 * 1024}>
      <Group justify="center" gap="xl" mih={120} style={{ pointerEvents: 'none' }}>
        <Dropzone.Accept>
          <IconUpload size={40} stroke={1.5} />
        </Dropzone.Accept>
        <Dropzone.Reject>
          <IconX size={40} stroke={1.5} />
        </Dropzone.Reject>
        <Dropzone.Idle>
          <IconFile size={40} stroke={1.5} />
        </Dropzone.Idle>
        <div>
          <Text size="lg" inline>
            Drag files here or click to browse
          </Text>
          <Text size="sm" c="dimmed" inline mt={7}>
            Max file size: 100MB
          </Text>
        </div>
      </Group>
    </Dropzone>
  );
}
