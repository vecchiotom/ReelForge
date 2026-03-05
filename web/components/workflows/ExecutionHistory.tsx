'use client';

import { Stack, Card, Group, Text, Loader, Center } from '@mantine/core';
import { useExecutions } from '@/lib/hooks/use-executions';
import { StatusBadge } from '@/components/projects/StatusBadge';
import { formatDate, formatDurationLong } from '@/lib/utils/format';
import { EmptyState } from '@/components/shared/EmptyState';
import Link from 'next/link';

interface ExecutionHistoryProps {
  projectId: string;
  workflowId: string;
}

export function ExecutionHistory({ projectId, workflowId }: ExecutionHistoryProps) {
  const { data: executions, isLoading } = useExecutions(projectId, workflowId);

  if (isLoading) return <Center py="md"><Loader size="sm" /></Center>;
  if (!executions || executions.length === 0) {
    return <EmptyState title="No executions" description="Execute this workflow to see results here." />;
  }

  return (
    <Stack gap="sm">
      <Text fw={600} size="sm">Execution History</Text>
      {executions.map((exec) => {
        const duration = exec.startedAt && exec.completedAt
          ? new Date(exec.completedAt).getTime() - new Date(exec.startedAt).getTime()
          : null;

        return (
          <Card
            key={exec.id}
            component={Link}
            href={`/projects/${projectId}/workflows/${workflowId}/executions/${exec.id}`}
            withBorder
            padding="sm"
            radius="md"
            style={{ textDecoration: 'none', cursor: 'pointer' }}
          >
            <Group justify="space-between">
              <Group gap="sm">
                <StatusBadge status={exec.status} />
                <Text size="xs" c="dimmed" ff="monospace">{exec.id.slice(0, 8)}</Text>
              </Group>
              <Group gap="sm">
                {exec.iterationCount > 0 && (
                  <Text size="xs" c="dimmed">{exec.iterationCount} iterations</Text>
                )}
                {duration !== null && (
                  <Text size="xs" c="dimmed">{formatDurationLong(duration)}</Text>
                )}
                {exec.startedAt && (
                  <Text size="xs" c="dimmed">{formatDate(exec.startedAt)}</Text>
                )}
              </Group>
            </Group>
          </Card>
        );
      })}
    </Stack>
  );
}
