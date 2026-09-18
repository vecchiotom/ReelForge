'use client';

import { useState } from 'react';
import { Paper, Group, Text, Badge, Button } from '@mantine/core';
import { IconPlayerPlay, IconCheck, IconX, IconClock, IconActivity, IconTool, IconBrain } from '@tabler/icons-react';
import { JsonViewer } from '@/components/workflows/JsonViewer';
import type { ExecutionStreamEvent } from '@/lib/hooks/use-execution-stream';

export function getPayloadString(payload: Record<string, unknown>, ...keys: string[]): string {
  for (const key of keys) {
    const value = payload[key];
    if (typeof value === 'string' && value.length > 0) {
      return value;
    }
  }

  return '';
}

export function getPayloadNumber(payload: Record<string, unknown>, ...keys: string[]): number {
  for (const key of keys) {
    const value = payload[key];
    if (typeof value === 'number' && Number.isFinite(value)) {
      return value;
    }
  }

  return 0;
}

export function getEventBadgeColor(eventType: string): string {
  if (eventType === 'execution.completed') {
    return 'green';
  }
  if (eventType === 'execution.failed') {
    return 'red';
  }
  if (eventType === 'execution.running') {
    return 'blue';
  }
  if (eventType === 'step.started') {
    return 'cyan';
  }
  if (eventType === 'step.progress') {
    return 'blue';
  }
  if (eventType === 'step.tool-called') {
    return 'indigo';
  }
  if (eventType === 'step.reasoning') {
    return 'grape';
  }

  return 'violet';
}

function getEventTitle(event: ExecutionStreamEvent): string {
  if (event.type === 'execution.running') return 'Workflow started';
  if (event.type === 'execution.completed') return 'Workflow completed';
  if (event.type === 'execution.failed') return 'Workflow failed';
  if (event.type === 'step.started') return 'Step started';
  if (event.type === 'step.progress') return getPayloadString(event.payload, 'stage', 'Stage') || 'Step progress';
  if (event.type === 'step.tool-called') return 'Tool call';
  if (event.type === 'step.reasoning') return 'Model reasoning';
  return 'Step completed';
}

export function getEventIcon(eventType: ExecutionStreamEvent['type']) {
  if (eventType === 'execution.completed') return <IconCheck size={12} />;
  if (eventType === 'execution.failed') return <IconX size={12} />;
  if (eventType === 'execution.running') return <IconActivity size={12} />;
  if (eventType === 'step.started') return <IconClock size={12} />;
  if (eventType === 'step.progress') return <IconActivity size={12} />;
  if (eventType === 'step.tool-called') return <IconTool size={12} />;
  if (eventType === 'step.reasoning') return <IconBrain size={12} />;
  return <IconPlayerPlay size={12} />;
}

function getEventMetadata(event: ExecutionStreamEvent): string[] {
  const metadata: string[] = [];

  const stepOrder = getPayloadNumber(event.payload, 'stepOrder', 'StepOrder');
  if (stepOrder > 0) {
    metadata.push(`Step #${stepOrder}`);
  }

  const stepLabel = getPayloadString(event.payload, 'stepLabel', 'StepLabel');
  if (stepLabel) {
    metadata.push(stepLabel);
  }

  const agentName = getPayloadString(event.payload, 'agentName', 'AgentName');
  if (agentName) {
    metadata.push(agentName);
  }

  if (event.type === 'step.completed') {
    const status = getPayloadString(event.payload, 'stepStatus', 'StepStatus');
    const durationMs = getPayloadNumber(event.payload, 'durationMs', 'DurationMs');
    const tokens = getPayloadNumber(event.payload, 'tokensUsed', 'TokensUsed');

    if (status) metadata.push(`Status: ${status}`);
    if (durationMs > 0) metadata.push(`${Math.round(durationMs)}ms`);
    if (tokens > 0) metadata.push(`${tokens.toLocaleString()} tokens`);
  }

  if (event.type === 'step.tool-called') {
    const toolName = getPayloadString(event.payload, 'toolName', 'ToolName');
    const sequence = getPayloadNumber(event.payload, 'sequence', 'Sequence');
    if (toolName) metadata.push(`Tool: ${toolName}`);
    if (sequence > 0) metadata.push(`Call #${sequence}`);
  }

  if (event.type === 'step.reasoning') {
    const sequence = getPayloadNumber(event.payload, 'sequence', 'Sequence');
    if (sequence > 0) metadata.push(`Reasoning #${sequence}`);
  }

  if (event.type === 'step.progress') {
    const percent = getPayloadNumber(event.payload, 'percentComplete', 'PercentComplete');
    if (percent > 0) metadata.push(`${Math.round(percent)}%`);
  }

  return metadata;
}

function getEventBody(event: ExecutionStreamEvent): string | null {
  if (event.type === 'execution.failed') {
    return getPayloadString(event.payload, 'errorMessage', 'ErrorMessage') || null;
  }

  if (event.type === 'step.tool-called') {
    return getPayloadString(event.payload, 'argumentsPreview', 'ArgumentsPreview')
      || getPayloadString(event.payload, 'resultPreview', 'ResultPreview')
      || null;
  }

  if (event.type === 'step.reasoning') {
    return getPayloadString(event.payload, 'content', 'Content') || null;
  }

  if (event.type === 'step.completed') {
    return getPayloadString(event.payload, 'errorDetails', 'ErrorDetails') || null;
  }

  return null;
}

export function ExecutionEventCard({ event }: { event: ExecutionStreamEvent }) {
  const [showRaw, setShowRaw] = useState(false);
  const metadata = getEventMetadata(event);
  const body = getEventBody(event);

  return (
    <Paper withBorder p="sm" radius="md">
      <Group justify="space-between" align="flex-start" mb={6}>
        <Group gap="xs" wrap="wrap">
          <Badge size="xs" color={getEventBadgeColor(event.type)}>{event.type}</Badge>
          <Text size="sm" fw={600}>{getEventTitle(event)}</Text>
        </Group>
        <Text size="xs" c="dimmed">{new Date(event.timestamp).toLocaleTimeString()}</Text>
      </Group>

      {metadata.length > 0 && (
        <Group gap={6} wrap="wrap" mb={body ? 6 : 0}>
          {metadata.map((item) => (
            <Badge key={item} size="xs" variant="light" color="gray">{item}</Badge>
          ))}
        </Group>
      )}

      {body && (
        <Text size="xs" c="dimmed" style={{ whiteSpace: 'pre-wrap' }} mb={8}>
          {body}
        </Text>
      )}

      <Button variant="subtle" size="compact-xs" onClick={() => setShowRaw((current) => !current)}>
        {showRaw ? 'Hide raw payload' : 'Show raw payload'}
      </Button>
      {showRaw && <JsonViewer label="Event payload" value={JSON.stringify(event.payload)} />}
    </Paper>
  );
}
