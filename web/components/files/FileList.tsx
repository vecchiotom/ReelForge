'use client';

import { Table, ActionIcon, Group } from '@mantine/core';
import { IconTrash, IconDownload, IconArrowsMove } from '@tabler/icons-react';
import { StatusBadge } from '@/components/projects/StatusBadge';
import { formatDate, formatFileSize } from '@/lib/utils/format';
import type { ProjectFile } from '@/lib/types/project';

interface FileListProps {
  files: ProjectFile[];
  onDelete: (fileId: string) => void;
  onSelect: (file: ProjectFile) => void;
  onDownload?: (file: ProjectFile) => void;
  onMove?: (file: ProjectFile) => void;
}

export function FileList({ files, onDelete, onSelect, onDownload, onMove }: FileListProps) {
  // show path-aware name; if an originalPath exists use it, otherwise fallback to the simple
  // file name. Indent the row based on the number of path segments to give a visual
  // directory structure. The list is sorted by that display string so folders cluster.
  const sorted = [...files].sort((a, b) => {
    const na = a.originalPath ?? a.originalFileName;
    const nb = b.originalPath ?? b.originalFileName;
    return na.localeCompare(nb, undefined, { sensitivity: 'base' });
  });

  const renderName = (file: ProjectFile) => {
    const display = file.originalPath ?? file.originalFileName;
    const indent = (display.split('/').length - 1) * 1.5;
    return (
      <span style={{ paddingLeft: `${indent}rem` }}>{display}</span>
    );
  };

  return (
    <Table striped highlightOnHover>
      <Table.Thead>
        <Table.Tr>
          <Table.Th>Name</Table.Th>
          <Table.Th>Type</Table.Th>
          <Table.Th>Size</Table.Th>
          <Table.Th>Summary</Table.Th>
          <Table.Th>Uploaded</Table.Th>
          <Table.Th />
        </Table.Tr>
      </Table.Thead>
      <Table.Tbody>
        {sorted.map((file) => (
          <Table.Tr key={file.id} onClick={() => onSelect(file)} style={{ cursor: 'pointer' }}>
            <Table.Td>{renderName(file)}</Table.Td>
            <Table.Td>{file.category}</Table.Td>
            <Table.Td>{formatFileSize(file.sizeBytes)}</Table.Td>
            <Table.Td><StatusBadge status={file.summaryStatus} /></Table.Td>
            <Table.Td>{formatDate(file.uploadedAt)}</Table.Td>
            <Table.Td>
              <Group justify="flex-end">
                {onDownload ? (
                  <ActionIcon
                    color="blue"
                    variant="subtle"
                    onClick={(e) => {
                      e.stopPropagation();
                      onDownload(file);
                    }}
                  >
                    <IconDownload size={16} />
                  </ActionIcon>
                ) : null}
                {onMove ? (
                  <ActionIcon
                    color="grape"
                    variant="subtle"
                    onClick={(e) => {
                      e.stopPropagation();
                      onMove(file);
                    }}
                  >
                    <IconArrowsMove size={16} />
                  </ActionIcon>
                ) : null}
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
