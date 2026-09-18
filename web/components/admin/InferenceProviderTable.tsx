'use client';

import { Table, Badge, ActionIcon, Group, Tooltip } from '@mantine/core';
import { IconEdit, IconTrash, IconCircleCheck, IconCircleX, IconCircleDashed } from '@tabler/icons-react';
import Link from 'next/link';
import type { InferenceProvider } from '@/lib/types/inference-provider';

interface InferenceProviderTableProps {
  providers: InferenceProvider[];
  onDelete: (provider: InferenceProvider) => void;
}

function LastTestIndicator({ provider }: { provider: InferenceProvider }) {
  if (provider.lastTestOk === null || provider.lastTestOk === undefined) {
    return (
      <Tooltip label="Never tested">
        <IconCircleDashed size={16} color="var(--mantine-color-gray-5)" />
      </Tooltip>
    );
  }
  if (provider.lastTestOk) {
    return (
      <Tooltip label={`Last test succeeded${provider.lastTestAt ? ` at ${new Date(provider.lastTestAt).toLocaleString()}` : ''}`}>
        <IconCircleCheck size={16} color="var(--mantine-color-green-6)" />
      </Tooltip>
    );
  }
  return (
    <Tooltip label={provider.lastTestError || 'Last test failed'}>
      <IconCircleX size={16} color="var(--mantine-color-red-6)" />
    </Tooltip>
  );
}

export function InferenceProviderTable({ providers, onDelete }: InferenceProviderTableProps) {
  return (
    <Table.ScrollContainer minWidth={700}>
      <Table striped highlightOnHover>
        <Table.Thead>
          <Table.Tr>
            <Table.Th>Name</Table.Th>
            <Table.Th>Kind</Table.Th>
            <Table.Th>Capability</Table.Th>
            <Table.Th visibleFrom="sm">Endpoint</Table.Th>
            <Table.Th>Model</Table.Th>
            <Table.Th>Default</Table.Th>
            <Table.Th visibleFrom="sm">Last Test</Table.Th>
            <Table.Th />
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {providers.map((provider) => (
            <Table.Tr key={provider.id}>
              <Table.Td>{provider.name}</Table.Td>
              <Table.Td>
                <Badge color={provider.kind === 'AzureOpenAI' ? 'blue' : 'grape'} variant="light" size="sm">
                  {provider.kind === 'AzureOpenAI' ? 'Azure OpenAI' : 'OpenAI-compatible'}
                </Badge>
              </Table.Td>
              <Table.Td>
                <Badge
                  color={
                    provider.capability === 'Transcription' ? 'teal' : provider.capability === 'Vision' ? 'orange' : 'indigo'
                  }
                  variant="light"
                  size="sm"
                >
                  {provider.capability}
                </Badge>
              </Table.Td>
              <Table.Td visibleFrom="sm" style={{ wordBreak: 'break-all' }}>{provider.endpoint}</Table.Td>
              <Table.Td>{provider.modelName}</Table.Td>
              <Table.Td>
                {provider.isDefault && (
                  <Badge color="violet" variant="light" size="sm">
                    Default
                  </Badge>
                )}
                {!provider.isEnabled && (
                  <Badge color="gray" variant="light" size="sm" ml={provider.isDefault ? 6 : 0}>
                    Disabled
                  </Badge>
                )}
              </Table.Td>
              <Table.Td visibleFrom="sm">
                <LastTestIndicator provider={provider} />
              </Table.Td>
              <Table.Td>
                <Group justify="flex-end" gap="xs">
                  <ActionIcon component={Link} href={`/admin/inference-providers/${provider.id}`} variant="subtle">
                    <IconEdit size={16} />
                  </ActionIcon>
                  <ActionIcon color="red" variant="subtle" onClick={() => onDelete(provider)}>
                    <IconTrash size={16} />
                  </ActionIcon>
                </Group>
              </Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
    </Table.ScrollContainer>
  );
}
