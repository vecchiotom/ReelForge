'use client';

import { useMemo, useState } from 'react';
import {
  Collapse,
  Badge,
  Button,
  Card,
  Group,
  Progress,
  ScrollArea,
  SimpleGrid,
  Stack,
  Table,
  Text,
} from '@mantine/core';
import {
  IconBrain,
  IconActivityHeartbeat,
  IconCheck,
  IconClock,
  IconInfoCircle,
  IconPlayerPlay,
  IconPlayerSkipForward,
  IconRefresh,
  IconShieldCheck,
  IconShieldX,
  IconTool,
  IconUsers,
  IconX,
} from '@tabler/icons-react';
import { useWorkflowMonitor } from '@/lib/hooks/use-workflow-monitor';
import type { WorkflowStreamEvent } from '@/lib/types/workflow-monitor';
import { JsonViewer } from '@/components/workflows/JsonViewer';
import styles from './page.module.css';

function formatTime(value: string | null): string {
  if (!value) {
    return 'No events yet';
  }

  return new Date(value).toLocaleTimeString();
}

function formatDuration(ms: number): string {
  if (!ms) {
    return '0ms';
  }
  if (ms < 1000) {
    return `${ms}ms`;
  }
  return `${(ms / 1000).toFixed(2)}s`;
}

function statusClass(status: 'running' | 'completed' | 'failed'): string {
  if (status === 'completed') {
    return styles.statusCompleted;
  }
  if (status === 'failed') {
    return styles.statusFailed;
  }
  return styles.statusRunning;
}

function packetToneClass(tone: 'queue' | 'step' | 'success' | 'failed'): string {
  if (tone === 'queue') {
    return styles.packetQueue;
  }
  if (tone === 'success') {
    return styles.packetSuccess;
  }
  if (tone === 'failed') {
    return styles.packetFailed;
  }
  return styles.packetStep;
}

function getNumber(payload: Record<string, unknown>, ...keys: string[]): number {
  for (const key of keys) {
    const value = payload[key];
    if (typeof value === 'number' && Number.isFinite(value)) {
      return value;
    }
  }

  return 0;
}

function getString(payload: Record<string, unknown>, ...keys: string[]): string {
  for (const key of keys) {
    const value = payload[key];
    if (typeof value === 'string' && value.length > 0) {
      return value;
    }
  }

  return '';
}

function getEventBadgeColor(type: WorkflowStreamEvent['type']): string {
  if (type === 'execution.completed') return 'teal';
  if (type === 'execution.failed') return 'red';
  if (type === 'execution.running') return 'blue';
  if (type === 'step.started') return 'cyan';
  if (type === 'step.tool-called') return 'indigo';
  if (type === 'step.reasoning') return 'grape';
  return 'violet';
}

function getEventIcon(type: WorkflowStreamEvent['type']) {
  if (type === 'execution.completed') return <IconCheck size={12} />;
  if (type === 'execution.failed') return <IconX size={12} />;
  if (type === 'execution.running') return <IconPlayerPlay size={12} />;
  if (type === 'step.started') return <IconClock size={12} />;
  if (type === 'step.completed') return <IconPlayerSkipForward size={12} />;
  if (type === 'step.tool-called') return <IconTool size={12} />;
  if (type === 'step.reasoning') return <IconBrain size={12} />;
  return <IconInfoCircle size={12} />;
}

function getEventTitle(type: WorkflowStreamEvent['type']): string {
  if (type === 'execution.running') return 'Execution Started';
  if (type === 'execution.completed') return 'Execution Completed';
  if (type === 'execution.failed') return 'Execution Failed';
  if (type === 'step.started') return 'Step Started';
  if (type === 'step.completed') return 'Step Completed';
  if (type === 'step.tool-called') return 'Tool Invocation';
  if (type === 'step.reasoning') return 'Model Reasoning';
  return 'Workflow Event';
}

function getEventMetadata(event: WorkflowStreamEvent): string[] {
  const metadata: string[] = [];
  const stepOrder = getNumber(event.payload, 'stepOrder', 'StepOrder');
  const stepLabel = getString(event.payload, 'stepLabel', 'StepLabel');
  const stepStatus = getString(event.payload, 'stepStatus', 'StepStatus');
  const agentName = getString(event.payload, 'agentName', 'AgentName');

  if (stepOrder > 0) metadata.push(`Step #${stepOrder}`);
  if (stepLabel) metadata.push(stepLabel);
  if (agentName) metadata.push(agentName);
  if (stepStatus) metadata.push(`Status: ${stepStatus}`);

  if (event.type === 'step.tool-called') {
    const toolName = getString(event.payload, 'toolName', 'ToolName');
    const sequence = getNumber(event.payload, 'sequence', 'Sequence');
    if (toolName) metadata.push(`Tool: ${toolName}`);
    if (sequence > 0) metadata.push(`Call #${sequence}`);
  }

  if (event.type === 'step.reasoning') {
    const sequence = getNumber(event.payload, 'sequence', 'Sequence');
    if (sequence > 0) metadata.push(`Reasoning #${sequence}`);
  }

  const durationMs = getNumber(event.payload, 'durationMs', 'DurationMs');
  const tokensUsed = getNumber(event.payload, 'tokensUsed', 'TokensUsed');
  if (durationMs > 0) metadata.push(formatDuration(durationMs));
  if (tokensUsed > 0) metadata.push(`${tokensUsed.toLocaleString()} tok`);

  return metadata;
}

function getEventBody(event: WorkflowStreamEvent): string {
  if (event.type === 'execution.failed') {
    return getString(event.payload, 'errorMessage', 'ErrorMessage') || 'Execution failed.';
  }

  if (event.type === 'step.tool-called') {
    return getString(event.payload, 'argumentsPreview', 'ArgumentsPreview')
      || getString(event.payload, 'resultPreview', 'ResultPreview')
      || 'Tool call captured.';
  }

  if (event.type === 'step.reasoning') {
    return getString(event.payload, 'content', 'Content') || 'Reasoning captured.';
  }

  if (event.type === 'step.started') {
    return getString(event.payload, 'inputPreview', 'InputPreview') || 'Step dispatching in workflow engine.';
  }

  if (event.type === 'step.completed') {
    const errorDetails = getString(event.payload, 'errorDetails', 'ErrorDetails');
    return errorDetails || 'Step completed successfully.';
  }

  return 'Execution telemetry event.';
}

function WarRoomEventCard({ event }: { event: WorkflowStreamEvent }) {
  const [expanded, setExpanded] = useState(false);
  const metadata = getEventMetadata(event);
  const body = getEventBody(event);

  return (
    <div className={styles.eventRow}>
      <Group justify="space-between" align="flex-start">
        <Group gap="xs">
          {getEventIcon(event.type)}
          <Text fw={600} size="sm">{getEventTitle(event.type)}</Text>
          <Badge size="xs" color={getEventBadgeColor(event.type)}>{event.type}</Badge>
        </Group>
        <Text className={styles.eventMeta}>{new Date(event.timestamp).toLocaleTimeString()}</Text>
      </Group>
      <Text size="xs" ff="monospace" mt={4}>exec: {event.executionId}</Text>
      <Text size="xs" c="dimmed" mt={4} style={{ whiteSpace: 'pre-wrap' }}>{body}</Text>
      {metadata.length > 0 && (
        <Group gap={6} mt={6} wrap="wrap">
          {metadata.map((item) => (
            <Badge key={`${event.id}-${item}`} size="xs" variant="light" color="gray">{item}</Badge>
          ))}
        </Group>
      )}
      <Button variant="subtle" size="compact-xs" mt={6} onClick={() => setExpanded((current) => !current)}>
        {expanded ? 'Hide raw payload' : 'Show raw payload'}
      </Button>
      <Collapse in={expanded}>
        <JsonViewer label="Event payload" value={JSON.stringify(event.payload)} />
      </Collapse>
    </div>
  );
}

export default function WorkflowServiceDashboardPage() {
  const monitor = useWorkflowMonitor();

  const eventInsights = useMemo(() => {
    const windowEvents = monitor.events.slice(0, 80);
    return {
      runningEvents: windowEvents.filter((event) => event.type === 'execution.running').length,
      stepStarted: windowEvents.filter((event) => event.type === 'step.started').length,
      stepCompleted: windowEvents.filter((event) => event.type === 'step.completed').length,
      toolCalls: windowEvents.filter((event) => event.type === 'step.tool-called').length,
      reasoning: windowEvents.filter((event) => event.type === 'step.reasoning').length,
      failures: windowEvents.filter((event) => event.type === 'execution.failed').length,
      avgStepTokens: (() => {
        const completed = windowEvents.filter((event) => event.type === 'step.completed');
        if (completed.length === 0) return 0;
        return Math.round(
          completed.reduce((sum, event) => sum + getNumber(event.payload, 'tokensUsed', 'TokensUsed'), 0) / completed.length,
        );
      })(),
    };
  }, [monitor.events]);

  return (
    <Stack className={styles.screen} gap="lg">
      <div className={styles.header}>
        <div>
          <h1 className={styles.title}>Workflow Service Command Center</h1>
          <Text className={styles.subtitle}>
            Live operational telemetry over SSE. Queue pressure, execution flow, and response dispatch in one war-room panel.
          </Text>
        </div>
        <Group>
          <Badge size="lg" color={monitor.connectionState === 'connected' ? 'teal' : 'yellow'} variant="light">
            Stream {monitor.connectionState}
          </Badge>
          <Button
            leftSection={<IconRefresh size={16} />}
            variant="light"
            onClick={() => monitor.refreshStats()}
          >
            Refresh Snapshot
          </Button>
        </Group>
      </div>

      <SimpleGrid cols={{ base: 1, sm: 2, xl: 6 }} className={styles.kpiGrid}>
        <Card className={styles.kpiCard} radius="md" p="md">
          <Text className={styles.kpiLabel}>Queued</Text>
          <Text className={styles.kpiValue}>{monitor.workflowStats?.queued ?? 0}</Text>
        </Card>
        <Card className={styles.kpiCard} radius="md" p="md">
          <Text className={styles.kpiLabel}>Running</Text>
          <Text className={styles.kpiValue}>{monitor.workflowStats?.active ?? 0}</Text>
        </Card>
        <Card className={styles.kpiCard} radius="md" p="md">
          <Text className={styles.kpiLabel}>Parallel Agents (Est.)</Text>
          <Text className={styles.kpiValue}>{monitor.derived.parallelAgentsEstimated}</Text>
        </Card>
        <Card className={styles.kpiCard} radius="md" p="md">
          <Text className={styles.kpiLabel}>Events / Min</Text>
          <Text className={styles.kpiValue}>{monitor.derived.eventsPerMinute}</Text>
        </Card>
        <Card className={styles.kpiCard} radius="md" p="md">
          <Text className={styles.kpiLabel}>Avg Step Duration</Text>
          <Text className={styles.kpiValue}>{formatDuration(monitor.derived.averageStepDurationMs)}</Text>
        </Card>
        <Card className={styles.kpiCard} radius="md" p="md">
          <Text className={styles.kpiLabel}>Token Rate / Min</Text>
          <Text className={styles.kpiValue}>{monitor.derived.tokenRatePerMinute}</Text>
        </Card>
      </SimpleGrid>

      <SimpleGrid cols={{ base: 2, md: 3, xl: 7 }} className={styles.kpiGrid}>
        <Card className={styles.kpiCard} radius="md" p="sm">
          <Text className={styles.kpiLabel}>Exec Started</Text>
          <Text className={styles.kpiValue}>{eventInsights.runningEvents}</Text>
        </Card>
        <Card className={styles.kpiCard} radius="md" p="sm">
          <Text className={styles.kpiLabel}>Step Started</Text>
          <Text className={styles.kpiValue}>{eventInsights.stepStarted}</Text>
        </Card>
        <Card className={styles.kpiCard} radius="md" p="sm">
          <Text className={styles.kpiLabel}>Step Completed</Text>
          <Text className={styles.kpiValue}>{eventInsights.stepCompleted}</Text>
        </Card>
        <Card className={styles.kpiCard} radius="md" p="sm">
          <Text className={styles.kpiLabel}>Tool Calls</Text>
          <Text className={styles.kpiValue}>{eventInsights.toolCalls}</Text>
        </Card>
        <Card className={styles.kpiCard} radius="md" p="sm">
          <Text className={styles.kpiLabel}>Reasoning Events</Text>
          <Text className={styles.kpiValue}>{eventInsights.reasoning}</Text>
        </Card>
        <Card className={styles.kpiCard} radius="md" p="sm">
          <Text className={styles.kpiLabel}>Failures</Text>
          <Text className={styles.kpiValue}>{eventInsights.failures}</Text>
        </Card>
        <Card className={styles.kpiCard} radius="md" p="sm">
          <Text className={styles.kpiLabel}>Avg Step Tokens</Text>
          <Text className={styles.kpiValue}>{eventInsights.avgStepTokens}</Text>
        </Card>
      </SimpleGrid>

      <section className={styles.surface}>
        <div className={styles.nodes}>
          <div className={styles.node}>Users</div>
          <div className={styles.node}>Message Queue</div>
          <div className={styles.node}>Workflow Engine</div>
          <div className={styles.node}>Responses</div>
        </div>
        <div className={styles.lanes}>
          <div className={styles.lane}>
            {monitor.derived.packets
              .filter((packet) => packet.lane === 'ingress')
              .map((packet) => (
                <span
                  key={packet.id}
                  className={`${styles.packet} ${packetToneClass(packet.tone)}`}
                  style={{ '--delay': `${packet.delayMs}ms` } as React.CSSProperties}
                />
              ))}
          </div>
          <div className={styles.lane}>
            {monitor.derived.packets
              .filter((packet) => packet.lane === 'execution')
              .map((packet) => (
                <span
                  key={packet.id}
                  className={`${styles.packet} ${packetToneClass(packet.tone)}`}
                  style={{ '--delay': `${packet.delayMs}ms` } as React.CSSProperties}
                />
              ))}
          </div>
          <div className={styles.lane}>
            {monitor.derived.packets
              .filter((packet) => packet.lane === 'egress')
              .map((packet) => (
                <span
                  key={packet.id}
                  className={`${styles.packet} ${packetToneClass(packet.tone)}`}
                  style={{ '--delay': `${packet.delayMs}ms` } as React.CSSProperties}
                />
              ))}
          </div>
        </div>
      </section>

      <SimpleGrid cols={{ base: 1, xl: 3 }}>
        <Card className={styles.panel} radius="md" p="md">
          <Text className={styles.sectionTitle}>Users and Policy State</Text>
          <Stack gap="sm">
            <Group justify="space-between">
              <Group gap="xs"><IconUsers size={16} /><Text>Total users</Text></Group>
              <Text fw={700}>{monitor.userStats.total}</Text>
            </Group>
            <Group justify="space-between">
              <Group gap="xs"><IconShieldCheck size={16} /><Text>Admins</Text></Group>
              <Text fw={700}>{monitor.userStats.admins}</Text>
            </Group>
            <Group justify="space-between">
              <Group gap="xs"><IconShieldX size={16} /><Text>Must rotate password</Text></Group>
              <Text fw={700}>{monitor.userStats.mustRotatePassword}</Text>
            </Group>
            <Text size="xs" c="dimmed">
              User-level execution attribution is not published by the current SSE payload; this panel tracks system-level response dispatch in aggregate.
            </Text>
            <Progress.Root size={14} radius="xl">
              <Progress.Section
                value={monitor.userStats.total === 0 ? 0 : (monitor.userStats.admins / monitor.userStats.total) * 100}
                color="cyan"
              />
              <Progress.Section
                value={
                  monitor.userStats.total === 0
                    ? 0
                    : (monitor.userStats.mustRotatePassword / monitor.userStats.total) * 100
                }
                color="orange"
              />
            </Progress.Root>
          </Stack>
        </Card>

        <Card className={styles.panel} radius="md" p="md">
          <Text className={styles.sectionTitle}>Active Executions</Text>
          <Table striped highlightOnHover withTableBorder>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Execution</Table.Th>
                <Table.Th>Status</Table.Th>
                <Table.Th>Last step</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {monitor.derived.activeExecutions.slice(0, 8).map((execution) => (
                <Table.Tr key={execution.executionId}>
                  <Table.Td>
                    <Text size="xs" ff="monospace">{execution.executionId.slice(0, 8)}</Text>
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm" className={statusClass(execution.status)}>
                      {execution.status}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <Group gap="xs" wrap="nowrap">
                      <Text size="xs">{execution.lastStepStatus ?? 'running'}</Text>
                      <Badge size="xs" variant="light" color="gray">{execution.lastEventType}</Badge>
                    </Group>
                  </Table.Td>
                </Table.Tr>
              ))}
              {monitor.derived.activeExecutions.length === 0 && (
                <Table.Tr>
                  <Table.Td colSpan={3}>
                    <Text size="sm" c="dimmed">No active executions in the recent window.</Text>
                  </Table.Td>
                </Table.Tr>
              )}
            </Table.Tbody>
          </Table>
          <Text size="xs" c="dimmed" mt="sm">
            Last event received at {formatTime(monitor.lastEventAt)}
          </Text>
        </Card>

        <Card className={styles.panel} radius="md" p="md">
          <Group justify="space-between" mb="sm">
            <Text className={styles.sectionTitle}>Live Event Feed</Text>
            <Group gap="xs">
              <IconActivityHeartbeat size={16} />
              <Text size="xs" c="dimmed">{monitor.events.length} buffered</Text>
            </Group>
          </Group>
          <ScrollArea className={styles.eventFeed} type="always" scrollbarSize={8}>
            <Stack gap="xs">
              {monitor.events.slice(0, 40).map((event) => (
                <WarRoomEventCard key={event.id} event={event} />
              ))}
              {monitor.events.length === 0 && (
                <Text size="sm" c="dimmed">Waiting for SSE events from `/api/v1/workflows/events`.</Text>
              )}
            </Stack>
          </ScrollArea>
        </Card>
      </SimpleGrid>
    </Stack>
  );
}
