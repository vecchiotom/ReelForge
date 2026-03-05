'use client';

import {
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
  IconActivityHeartbeat,
  IconRefresh,
  IconRocket,
  IconShieldCheck,
  IconShieldX,
  IconUsers,
} from '@tabler/icons-react';
import { useWorkflowMonitor } from '@/lib/hooks/use-workflow-monitor';
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

export default function WorkflowServiceDashboardPage() {
  const monitor = useWorkflowMonitor();

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
                    <Text size="xs">{execution.lastStepStatus ?? 'running'}</Text>
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
                <div key={event.id} className={styles.eventRow}>
                  <Group justify="space-between" align="flex-start">
                    <Group gap="xs">
                      <IconRocket size={14} />
                      <Text fw={600} size="sm">{event.type}</Text>
                    </Group>
                    <Text className={styles.eventMeta}>{new Date(event.timestamp).toLocaleTimeString()}</Text>
                  </Group>
                  <Text size="xs" ff="monospace">exec: {event.executionId}</Text>
                  <Text size="xs" c="dimmed">
                    correlation: {String(event.payload.correlationId ?? 'n/a')} | tokens: {String(event.payload.tokensUsed ?? 0)} | duration: {String(event.payload.durationMs ?? 0)}ms
                  </Text>
                </div>
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
