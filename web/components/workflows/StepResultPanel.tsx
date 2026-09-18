'use client';

import { Alert, Badge, Card, Group, Paper, ScrollArea, Stack, Tabs, Text } from '@mantine/core';
import { IconAlertCircle } from '@tabler/icons-react';
import type { WorkflowStepResult } from '@/lib/types/workflow';
import { formatDate } from '@/lib/utils/format';
import { getOutputVideoUrl } from '@/lib/api/outputs';
import { JsonViewer } from '@/components/workflows/JsonViewer';
import { ExecutionEventCard } from '@/components/workflows/ExecutionEventCard';
import type { ExecutionStreamEvent } from '@/lib/hooks/use-execution-stream';

interface StepResultLiveTokenMetrics {
  inputTokens: number;
  outputTokens: number;
}

export interface StepResultPanelProps {
  projectId: string;
  stepResult: WorkflowStepResult;
  stepEvents: ExecutionStreamEvent[];
  liveTokenMetrics: StepResultLiveTokenMetrics;
  artifactJson: string | null;
  artifactError: string | null;
  artifactLoading: boolean;
}

function statusColor(status: WorkflowStepResult['status']): string {
  if (status === 'Completed') return 'green';
  if (status === 'Running') return 'blue';
  if (status === 'Failed') return 'red';
  return 'gray';
}

export function StepResultPanel({
  projectId,
  stepResult,
  stepEvents,
  liveTokenMetrics,
  artifactJson,
  artifactError,
  artifactLoading,
}: StepResultPanelProps) {
  const hasError = !!stepResult.errorDetails;
  const hasIterationOrCompletion =
    (stepResult.iterationNumber !== null && stepResult.iterationNumber !== undefined) || !!stepResult.completedAt;

  return (
    <Stack gap="md">
      <Group justify="space-between">
        <Badge size="lg" variant="light" color={statusColor(stepResult.status)}>
          {stepResult.status ?? 'Pending'}
        </Badge>
        <Text size="sm" c="dimmed">{formatDate(stepResult.executedAt)}</Text>
      </Group>

      <Group grow>
        <Paper withBorder p="sm" radius="md">
          <Text size="xs" c="dimmed">Duration</Text>
          <Text fw={700}>{Math.round(stepResult.durationMs)}ms</Text>
        </Paper>
        <Paper withBorder p="sm" radius="md">
          <Text size="xs" c="dimmed">Tokens Used</Text>
          <Text fw={700}>{stepResult.tokensUsed.toLocaleString()}</Text>
        </Paper>
      </Group>
      <Group grow>
        <Paper withBorder p="sm" radius="md">
          <Text size="xs" c="dimmed">Input Tokens (live)</Text>
          <Text fw={700}>{liveTokenMetrics.inputTokens.toLocaleString()}</Text>
        </Paper>
        <Paper withBorder p="sm" radius="md">
          <Text size="xs" c="dimmed">Output Tokens (live)</Text>
          <Text fw={700}>{liveTokenMetrics.outputTokens.toLocaleString()}</Text>
        </Paper>
      </Group>

      <Tabs defaultValue="overview" keepMounted={false}>
        <Tabs.List>
          <Tabs.Tab value="overview">
            <Group gap={6} wrap="nowrap">
              <Text size="sm">Overview</Text>
              {hasError && (
                <span
                  aria-label="Step has error details"
                  style={{
                    width: 6,
                    height: 6,
                    borderRadius: '50%',
                    background: 'var(--mantine-color-red-6)',
                    display: 'inline-block',
                  }}
                />
              )}
            </Group>
          </Tabs.Tab>
          <Tabs.Tab value="data">Data</Tabs.Tab>
          <Tabs.Tab value="events">Events</Tabs.Tab>
        </Tabs.List>

        <ScrollArea.Autosize mah={520} mt="sm">
          <Tabs.Panel value="overview">
            <Stack gap="md">
              {hasError && (
                <Alert icon={<IconAlertCircle size={16} />} title="Error Details" color="red" variant="light">
                  <Text size="sm" style={{ whiteSpace: 'pre-wrap' }}>{stepResult.errorDetails}</Text>
                </Alert>
              )}

              {hasIterationOrCompletion && (
                <Card withBorder padding="sm" radius="md">
                  <Stack gap="xs">
                    {stepResult.iterationNumber !== null && stepResult.iterationNumber !== undefined && (
                      <Group justify="space-between">
                        <Text size="sm" c="dimmed">Iteration</Text>
                        <Text size="sm" fw={500}>#{stepResult.iterationNumber}</Text>
                      </Group>
                    )}
                    {stepResult.completedAt && (
                      <Group justify="space-between">
                        <Text size="sm" c="dimmed">Completed At</Text>
                        <Text size="sm" fw={500}>{formatDate(stepResult.completedAt)}</Text>
                      </Group>
                    )}
                  </Stack>
                </Card>
              )}

              {stepResult.outputStorageKey && (
                <Card withBorder padding="sm" radius="md">
                  <Text size="sm" fw={600} mb="xs">Output Video</Text>
                  <video
                    controls
                    style={{ width: '100%', borderRadius: 8 }}
                    src={getOutputVideoUrl(projectId, stepResult.id)}
                  />
                </Card>
              )}

              <Text size="sm" style={{ whiteSpace: 'pre-wrap' }}>
                {stepResult.output || 'No output available'}
              </Text>
            </Stack>
          </Tabs.Panel>

          <Tabs.Panel value="data">
            <Stack gap="md">
              {stepResult.artifactStorageKey && (
                <Card withBorder padding="sm" radius="md">
                  <Text size="sm" fw={600} mb="xs">Edit Decision Artifact</Text>
                  {artifactLoading && <Text size="sm" c="dimmed">Loading artifact...</Text>}
                  {artifactError && (
                    <Alert icon={<IconAlertCircle size={14} />} color="yellow" variant="light">
                      Could not load artifact: {artifactError}
                    </Alert>
                  )}
                  {artifactJson && <JsonViewer label="Artifact JSON" value={artifactJson} />}
                </Card>
              )}

              {stepResult.inputJson && (
                <Card withBorder padding="sm" radius="md">
                  <JsonViewer label="Input JSON" value={stepResult.inputJson} />
                </Card>
              )}

              {stepResult.outputJson && (
                <Card withBorder padding="sm" radius="md">
                  <JsonViewer label="Output JSON" value={stepResult.outputJson} />
                </Card>
              )}

              {!stepResult.artifactStorageKey && !stepResult.inputJson && !stepResult.outputJson && (
                <Text size="sm" c="dimmed">No structured data available for this step.</Text>
              )}
            </Stack>
          </Tabs.Panel>

          <Tabs.Panel value="events">
            <Stack gap="xs">
              {stepEvents.length === 0 ? (
                <Text size="sm" c="dimmed">No live updates yet for this step.</Text>
              ) : (
                stepEvents.map((event, index) => (
                  <ExecutionEventCard key={`${event.id}-${index}`} event={event} />
                ))
              )}
            </Stack>
          </Tabs.Panel>
        </ScrollArea.Autosize>
      </Tabs>
    </Stack>
  );
}
