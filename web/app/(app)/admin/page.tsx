'use client';

import { Card, Text, Stack, Group, Badge, Loader, Center } from '@mantine/core';
import { IconServer } from '@tabler/icons-react';
import Link from 'next/link';
import { PageHeader } from '@/components/shared/PageHeader';
import { useWorkflowEngineStatus } from '@/lib/hooks/use-workflow-engine';
import { formatDate } from '@/lib/utils/format';

export default function AdminOverviewPage() {
  const { data: engineStatus, isLoading, error } = useWorkflowEngineStatus();

  return (
    <>
      <PageHeader
        title="Admin Overview"
        breadcrumbs={[{ label: 'Admin' }, { label: 'Overview' }]}
      />
      <Stack gap="md" maw={600}>
        <Card
          withBorder
          padding="md"
          radius="md"
          component={Link}
          href="/admin/workflow-service"
          style={{ cursor: 'pointer' }}
        >
          <Group justify="space-between" mb="sm">
            <Group gap="sm">
              <IconServer size={20} />
              <Text fw={600}>Workflow Engine</Text>
            </Group>
            {isLoading ? (
              <Loader size="xs" />
            ) : error ? (
              <Badge color="red" variant="filled">Unreachable</Badge>
            ) : (
              <Badge color="green" variant="filled">Healthy</Badge>
            )}
          </Group>
          {engineStatus && (
            <Stack gap={4}>
              <Text size="sm" c="dimmed">Service: {engineStatus.service}</Text>
              <Text size="sm" c="dimmed">Status: {engineStatus.status}</Text>
              <Text size="sm" c="dimmed">Last checked: {formatDate(engineStatus.timestamp)}</Text>
            </Stack>
          )}
          {error && !isLoading && (
            <Text size="sm" c="red">Could not connect to the Workflow Engine service.</Text>
          )}
          <Text size="sm" mt="sm" c="cyan.4">Open live command center</Text>
        </Card>
      </Stack>
    </>
  );
}
