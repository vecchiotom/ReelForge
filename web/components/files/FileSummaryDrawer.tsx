'use client';

import { Drawer, Text, Stack, Badge, Code } from '@mantine/core';
import { StatusBadge } from '@/components/projects/StatusBadge';
import type { ProjectFile } from '@/lib/types/project';

interface FileSummaryDrawerProps {
  file: ProjectFile | null;
  opened: boolean;
  onClose: () => void;
}

export function FileSummaryDrawer({ file, opened, onClose }: FileSummaryDrawerProps) {
  if (!file) return null;

  const title = file.originalPath ? file.originalPath : file.originalFileName;
  return (
    <Drawer opened={opened} onClose={onClose} title={title} position="right" size="lg">
      <Stack gap="md">
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
      </Stack>
    </Drawer>
  );
}
