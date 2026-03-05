'use client';

import { Table, ActionIcon, Group } from '@mantine/core';
import { IconTrash } from '@tabler/icons-react';
import { StatusBadge } from '@/components/projects/StatusBadge';
import { formatDate, formatFileSize } from '@/lib/utils/format';
import type { ProjectFile } from '@/lib/types/project';

interface FileListProps {
  files: ProjectFile[];
  onDelete: (fileId: string) => void;
  onSelect: (file: ProjectFile) => void;
}

export function FileList({ files, onDelete, onSelect }: FileListProps) {
  return (
    <Table striped highlightOnHover>
      <Table.Thead>
        <Table.Tr>
          <Table.Th>Name</Table.Th>
          <Table.Th>Size</Table.Th>
          <Table.Th>Summary</Table.Th>
          <Table.Th>Uploaded</Table.Th>
          <Table.Th />
        </Table.Tr>
      </Table.Thead>
      <Table.Tbody>
        {files.map((file) => (
          <Table.Tr key={file.id} onClick={() => onSelect(file)} style={{ cursor: 'pointer' }}>
            <Table.Td>{file.originalFileName}</Table.Td>
            <Table.Td>{formatFileSize(file.sizeBytes)}</Table.Td>
            <Table.Td><StatusBadge status={file.summaryStatus} /></Table.Td>
            <Table.Td>{formatDate(file.uploadedAt)}</Table.Td>
            <Table.Td>
              <Group justify="flex-end">
                <ActionIcon
                  color="red"
                  variant="subtle"
                  onClick={(e) => {
                    e.stopPropagation();
                    onDelete(file.id);
                  }}
                >
                  <IconTrash size={16} />
                </ActionIcon>
              </Group>
            </Table.Td>
          </Table.Tr>
        ))}
      </Table.Tbody>
    </Table>
  );
}
