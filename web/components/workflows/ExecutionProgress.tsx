'use client';

import { Timeline, Text, Badge, Card, Stack, Group, Loader, Alert } from '@mantine/core';
import { IconCheck, IconX, IconClock, IconPlayerSkipForward, IconAlertCircle, IconBolt } from '@tabler/icons-react';
import { StatusBadge } from '@/components/projects/StatusBadge';
import { StepTypeBadge } from './StepTypeBadge';
import { JsonViewer } from './JsonViewer';
import { formatDuration, formatDate } from '@/lib/utils/format';
import type { WorkflowExecution, WorkflowDefinition, StepType } from '@/lib/types/workflow';

function getStepIcon(status: string) {
  switch (status) {
    case 'Completed': return <IconCheck size={14} />;
    case 'Failed': return <IconX size={14} />;
    case 'Running': return <Loader size={14} />;
    case 'Skipped': return <IconPlayerSkipForward size={14} />;
    default: return <IconClock size={14} />;
  }
}

function getStepColor(status: string) {
  switch (status) {
    case 'Completed': return 'green';
    case 'Failed': return 'red';
    case 'Running': return 'blue';
    case 'Skipped': return 'yellow';
    default: return 'gray';
  }
}

interface ExecutionProgressProps {
  execution: WorkflowExecution;
  workflow?: WorkflowDefinition;
}

export function ExecutionProgress({ execution, workflow }: ExecutionProgressProps) {
  const stepMap = new Map(
    workflow?.steps.map((s) => [s.id, s]) || [],
  );

  const totalTokens = execution.stepResults.reduce((sum, r) => sum + r.tokensUsed, 0);
  const totalDuration = execution.stepResults.reduce((sum, r) => sum + r.durationMs, 0);
  // Steps served from the cross-execution step-result cache spent nothing this run, so they
  // contribute 0 to totalTokens above; cachedTokensSaved carries what the ORIGINAL run cost.
  const cachedSteps = execution.stepResults.filter((r) => r.fromCache);
  const tokensSaved = cachedSteps.reduce((sum, r) => sum + (r.cachedTokensSaved ?? 0), 0);

  return (
    <Stack gap="md">
      <Group gap="sm">
        <StatusBadge status={execution.status} />
        {execution.startedAt && <Text size="xs" c="dimmed">Started {formatDate(execution.startedAt)}</Text>}
        {execution.completedAt && <Text size="xs" c="dimmed">Completed {formatDate(execution.completedAt)}</Text>}
        {execution.correlationId && (
          <Badge variant="outline" size="sm" ff="monospace">
            {execution.correlationId.slice(0, 8)}
          </Badge>
        )}
        {execution.iterationCount > 0 && (
          <Badge variant="light" size="sm">
            {execution.iterationCount} iterations
          </Badge>
        )}
      </Group>

      {execution.errorMessage && (
        <Alert color="red" icon={<IconAlertCircle size={16} />} title="Execution Error">
          {execution.errorMessage}
        </Alert>
      )}

      {execution.reviewScores.length > 0 && (
        <Group gap="xs">
          <Text size="sm" fw={500}>Review Scores:</Text>
          {execution.reviewScores.map((rs) => (
            <Badge key={rs.id} color={rs.score >= 9 ? 'green' : 'orange'} variant="light">
              Iteration {rs.iterationNumber}: {rs.score}/10
            </Badge>
          ))}
        </Group>
      )}

      <Timeline active={execution.stepResults.filter((s) => s.status === 'Completed').length - 1} bulletSize={24}>
        {execution.stepResults.map((step) => {
          const workflowStep = stepMap.get(step.workflowStepId);
          const stepLabel = workflowStep?.label || 'Step';
          const stepType = (workflowStep?.stepType || 'Agent') as StepType;

          return (
            <Timeline.Item
              key={step.id}
              bullet={getStepIcon(step.status || 'Pending')}
              color={getStepColor(step.status || 'Pending')}
              title={
                <Group gap="xs">
                  <Text size="sm" fw={500}>{stepLabel}</Text>
                  <StepTypeBadge stepType={stepType} />
                  <StatusBadge status={step.status || 'Pending'} />
                  {step.iterationNumber != null && (
                    <Badge size="xs" variant="outline">iter {step.iterationNumber}</Badge>
                  )}
                  {step.durationMs > 0 && <Text size="xs" c="dimmed">{formatDuration(step.durationMs)}</Text>}
                  {step.tokensUsed > 0 && <Text size="xs" c="dimmed">{step.tokensUsed} tokens</Text>}
                  {step.fromCache && (
                    <Badge
                      size="xs"
                      variant="light"
                      color="teal"
                      leftSection={<IconBolt size={10} />}
                      title={
                        step.cachedTokensSaved
                          ? `Reused from a previous execution — saved ${step.cachedTokensSaved.toLocaleString()} tokens`
                          : 'Reused from a previous execution'
                      }
                    >
                      cached
                    </Badge>
                  )}
                </Group>
              }
            >
              <Stack gap="xs" mt="xs">
                {step.errorDetails && (
                  <Alert color="red" icon={<IconAlertCircle size={14} />} p="xs">
                    <Text size="xs">{step.errorDetails}</Text>
                  </Alert>
                )}
                <JsonViewer label="Input" value={step.inputJson} />
                <JsonViewer label="Output" value={step.outputJson} />
                {step.output && !step.outputJson && (
                  <Card withBorder p="xs">
                    <Text size="xs" style={{ whiteSpace: 'pre-wrap', maxHeight: 200, overflow: 'auto' }}>
                      {step.output}
                    </Text>
                  </Card>
                )}
              </Stack>
            </Timeline.Item>
          );
        })}
      </Timeline>

      {execution.stepResults.length > 0 && (
        <Group gap="md">
          <Text size="xs" c="dimmed">Total: {totalTokens} tokens</Text>
          <Text size="xs" c="dimmed">Total: {formatDuration(totalDuration)}</Text>
          {cachedSteps.length > 0 && (
            <Text size="xs" c="teal">
              {cachedSteps.length} step{cachedSteps.length === 1 ? '' : 's'} reused from cache
              {tokensSaved > 0 && ` — saved ${tokensSaved.toLocaleString()} tokens`}
            </Text>
          )}
        </Group>
      )}
    </Stack>
  );
}
