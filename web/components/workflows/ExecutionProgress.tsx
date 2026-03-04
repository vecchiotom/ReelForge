'use client';

import { Timeline, Text, Badge, Card, Code, Stack, Group, Loader } from '@mantine/core';
import { IconCheck, IconX, IconClock, IconPlayerPlay } from '@tabler/icons-react';
import { StatusBadge } from '@/components/projects/StatusBadge';
import { formatDuration, formatDate } from '@/lib/utils/format';
import type { WorkflowExecution } from '@/lib/types/workflow';

function getStepIcon(status: string) {
  switch (status) {
    case 'Completed': return <IconCheck size={14} />;
    case 'Failed': return <IconX size={14} />;
    case 'Running': return <Loader size={14} />;
    default: return <IconClock size={14} />;
  }
}

function getStepColor(status: string) {
  switch (status) {
    case 'Completed': return 'green';
    case 'Failed': return 'red';
    case 'Running': return 'blue';
    default: return 'gray';
  }
}

export function ExecutionProgress({ execution }: { execution: WorkflowExecution }) {
  return (
    <Stack gap="md">
      <Group>
        <StatusBadge status={execution.status} />
        {execution.startedAt && <Text size="xs" c="dimmed">Started {formatDate(execution.startedAt)}</Text>}
        {execution.completedAt && <Text size="xs" c="dimmed">Completed {formatDate(execution.completedAt)}</Text>}
      </Group>

      {execution.reviewScores.length > 0 && (
        <Group gap="xs">
          <Text size="sm" fw={500}>Review Scores:</Text>
          {execution.reviewScores.map((rs) => (
            <Badge key={rs.id} color={rs.score >= 9 ? 'green' : 'orange'} variant="light">
              Iteration {rs.iteration}: {rs.score}/10
            </Badge>
          ))}
        </Group>
      )}

      <Timeline active={execution.stepResults.filter((s) => s.status === 'Completed').length - 1} bulletSize={24}>
        {execution.stepResults.map((step) => (
          <Timeline.Item
            key={step.id}
            bullet={getStepIcon(step.status)}
            color={getStepColor(step.status)}
            title={
              <Group gap="xs">
                <Text size="sm" fw={500}>Step</Text>
                <StatusBadge status={step.status} />
                {step.durationMs > 0 && <Text size="xs" c="dimmed">{formatDuration(step.durationMs)}</Text>}
                {step.tokensUsed > 0 && <Text size="xs" c="dimmed">{step.tokensUsed} tokens</Text>}
              </Group>
            }
          >
            {step.output && (
              <Card withBorder mt="xs" p="xs">
                <Code block style={{ whiteSpace: 'pre-wrap', maxHeight: 200, overflow: 'auto' }}>
                  {step.output}
                </Code>
              </Card>
            )}
          </Timeline.Item>
        ))}
      </Timeline>
    </Stack>
  );
}
