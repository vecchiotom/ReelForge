'use client';

import { useState } from 'react';
import { Text, Group } from '@mantine/core';
import { Dropzone } from '@mantine/dropzone';
import { IconUpload, IconX, IconFile } from '@tabler/icons-react';
import { notifications } from '@mantine/notifications';
import { uploadFileWithProgress } from '@/lib/api/files';
import { UploadProgressList, UploadItem } from './UploadProgressList';

interface FileUploadZoneProps {
  projectId: string;
  onSuccess: () => void;
}

export function FileUploadZone({ projectId, onSuccess }: FileUploadZoneProps) {
  const [uploads, setUploads] = useState<UploadItem[]>([]);

  const handleDrop = async (files: File[]) => {
    // create upload entries for each file
    const newItems: UploadItem[] = files.map((f) => ({
      id: crypto.randomUUID(),
      file: f,
      progress: 0,
      status: 'uploading',
    }));
    setUploads((u) => [...u, ...newItems]);

    // start each upload in parallel
    await Promise.all(
      newItems.map(async (item) => {
        try {
          const result = await uploadFileWithProgress(
            projectId,
            item.file,
            (pct) => {
              setUploads((u) =>
                u.map((x) =>
                  x.id === item.id ? { ...x, progress: pct } : x,
                ),
              );
            },
          );
          // mark done
          setUploads((u) =>
            u.map((x) =>
              x.id === item.id ? { ...x, progress: 100, status: 'done' } : x,
            ),
          );
          onSuccess();
        } catch (err: unknown) {
          setUploads((u) =>
            u.map((x) =>
              x.id === item.id
                ? {
                    ...x,
                    status: 'error',
                    error: err instanceof Error ? err.message : String(err),
                  }
                : x,
            ),
          );
          notifications.show({
            title: 'Upload failed',
            message: err instanceof Error ? err.message : 'Upload failed',
            color: 'red',
          });
        }
      }),
    );
  };

  const anyUploading = uploads.some((u) => u.status === 'uploading');

  return (
    <>
      <Dropzone
        onDrop={handleDrop}
        loading={anyUploading}
        maxSize={100 * 1024 * 1024}
        inputProps={{
          ...( { webkitdirectory: '', directory: '' } as any ),
          multiple: true,
        }}
      >
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
              Drag files or folders here, or click to browse
            </Text>
            <Text size="sm" c="dimmed" inline mt={7}>
              Max file size: 100MB
            </Text>
          </div>
        </Group>
      </Dropzone>
      <UploadProgressList uploads={uploads} setUploads={setUploads} />
    </>
  );
}
