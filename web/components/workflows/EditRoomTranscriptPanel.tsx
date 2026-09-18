'use client';

import { useEffect, useMemo, useRef, useState } from 'react';
import { Alert, Avatar, Badge, Group, Paper, ScrollArea, Stack, Text } from '@mantine/core';
import { IconAlertCircle, IconGavel, IconMessage2, IconUsers } from '@tabler/icons-react';
import type { WorkflowStepChatTurn, WorkflowStepResult } from '@/lib/types/workflow';
import type { ExecutionStreamEvent } from '@/lib/hooks/use-execution-stream';
import { getPayloadNumber, getPayloadString } from '@/components/workflows/ExecutionEventCard';
import { JsonViewer } from '@/components/workflows/JsonViewer';

// Distinct, readable Mantine theme colors to hash a seat name onto. Deliberately excludes
// gray/dark (reserved for the "thinking" placeholder) and yellow (reserved for the Director).
const SEAT_COLORS = [
  'blue', 'cyan', 'teal', 'green', 'lime', 'orange', 'red', 'pink', 'grape', 'violet', 'indigo',
] as const;

function hashSpeakerColor(speaker: string): string {
  let hash = 0;
  for (let i = 0; i < speaker.length; i++) {
    hash = (hash * 31 + speaker.charCodeAt(i)) >>> 0;
  }
  return SEAT_COLORS[hash % SEAT_COLORS.length];
}

function getInitials(name: string): string {
  const words = name.match(/[A-Z][a-z0-9]*|[a-z0-9]+/g) ?? [];
  if (words.length >= 2) {
    const first = words[0]?.[0] ?? '';
    const second = words[1]?.[0] ?? '';
    return (first + second).toUpperCase() || '?';
  }
  if (words.length === 1) {
    return (words[0] ?? '').slice(0, 2).toUpperCase() || '?';
  }
  return name.slice(0, 2).toUpperCase() || '?';
}

function getPayloadBoolean(payload: Record<string, unknown>, ...keys: string[]): boolean {
  for (const key of keys) {
    const value = payload[key];
    if (typeof value === 'boolean') {
      return value;
    }
  }
  return false;
}

function getPayloadStringArray(payload: Record<string, unknown>, ...keys: string[]): string[] {
  for (const key of keys) {
    const value = payload[key];
    if (Array.isArray(value)) {
      return value.filter((item): item is string => typeof item === 'string');
    }
  }
  return [];
}

function getPayloadNullableNumber(payload: Record<string, unknown>, ...keys: string[]): number | null {
  for (const key of keys) {
    const value = payload[key];
    if (typeof value === 'number' && Number.isFinite(value)) {
      return value;
    }
  }
  return null;
}

function parseChatTurn(event: ExecutionStreamEvent): WorkflowStepChatTurn | null {
  const payload = event.payload;
  const speaker = getPayloadString(payload, 'speaker', 'Speaker');
  if (!speaker) {
    // Malformed/incomplete event — skip defensively rather than rendering a blank bubble.
    return null;
  }

  const speakerRoleRaw = getPayloadString(payload, 'speakerRole', 'SpeakerRole').toLowerCase();

  return {
    executionId: getPayloadString(payload, 'executionId', 'ExecutionId'),
    stepId: getPayloadString(payload, 'stepId', 'StepId'),
    stepResultId: getPayloadString(payload, 'stepResultId', 'StepResultId'),
    projectId: getPayloadString(payload, 'projectId', 'ProjectId'),
    workflowDefinitionId: getPayloadString(payload, 'workflowDefinitionId', 'WorkflowDefinitionId'),
    stepOrder: getPayloadNumber(payload, 'stepOrder', 'StepOrder'),
    stepLabel: getPayloadString(payload, 'stepLabel', 'StepLabel'),
    correlationId: getPayloadString(payload, 'correlationId', 'CorrelationId'),
    turnIndex: getPayloadNumber(payload, 'turnIndex', 'TurnIndex'),
    totalTurns: getPayloadNullableNumber(payload, 'totalTurns', 'TotalTurns'),
    speaker,
    speakerRole: speakerRoleRaw === 'director' ? 'director' : 'editor',
    text: getPayloadString(payload, 'text', 'Text'),
    truncated: getPayloadBoolean(payload, 'truncated', 'Truncated'),
    idsMentioned: getPayloadStringArray(payload, 'idsMentioned', 'IdsMentioned'),
    occurredAt: getPayloadString(payload, 'occurredAt', 'OccurredAt') || event.timestamp,
  };
}

function ChatBubble({ turn }: { turn: WorkflowStepChatTurn }) {
  const isDirector = turn.speakerRole === 'director';
  const color = isDirector ? 'yellow' : hashSpeakerColor(turn.speaker);
  const initials = getInitials(turn.speaker);
  const time = new Date(turn.occurredAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  const turnLabel = turn.totalTurns ? `Turn ${turn.turnIndex + 1} of ${turn.totalTurns}` : `Turn ${turn.turnIndex + 1}`;

  return (
    <Paper
      withBorder
      radius="md"
      p="sm"
      style={{
        borderLeft: `3px solid var(--mantine-color-${color}-6)`,
        background: isDirector ? 'var(--mantine-color-yellow-light)' : undefined,
      }}
    >
      <Group justify="space-between" align="flex-start" wrap="nowrap" mb={6}>
        <Group gap="xs" wrap="nowrap" align="flex-start">
          <Avatar color={color} radius={isDirector ? 'md' : 'xl'} variant={isDirector ? 'filled' : 'light'} size="sm">
            {isDirector ? <IconGavel size={14} /> : initials}
          </Avatar>
          <div>
            <Group gap={6}>
              <Text size="sm" fw={700}>{turn.speaker}</Text>
              {isDirector && <Badge size="xs" color="yellow" variant="filled">Director</Badge>}
            </Group>
            <Text size="xs" c="dimmed">{turnLabel} · {time}</Text>
          </div>
        </Group>
      </Group>

      <Text size="sm" style={{ whiteSpace: 'pre-wrap' }}>
        {turn.text}
        {turn.truncated && '…'}
      </Text>

      {turn.truncated && (
        <Text size="xs" c="dimmed" fs="italic" mt={4}>
          Truncated — full turn text is in the room&apos;s transcript artifact.
        </Text>
      )}

      {turn.idsMentioned.length > 0 && (
        <Group gap={4} mt={8} wrap="wrap">
          {turn.idsMentioned.map((id) => (
            <Badge key={id} size="xs" variant="dot" color="gray">{id}</Badge>
          ))}
        </Group>
      )}
    </Paper>
  );
}

function ThinkingBubble() {
  return (
    <Paper withBorder radius="md" p="sm" style={{ opacity: 0.75 }}>
      <Group gap={8} wrap="nowrap">
        <Avatar radius="xl" size="sm" color="gray" variant="light">
          <IconMessage2 size={14} />
        </Avatar>
        <Group gap={6} wrap="nowrap">
          <Text size="xs" c="dimmed" fs="italic">The room is thinking</Text>
          <span className="editroom-thinking-dots" aria-hidden="true">
            <span />
            <span />
            <span />
          </span>
        </Group>
      </Group>
    </Paper>
  );
}

export interface EditRoomTranscriptPanelProps {
  stepResult: WorkflowStepResult;
  stepEvents: ExecutionStreamEvent[];
  artifactJson?: string | null;
  artifactError?: string | null;
  artifactLoading?: boolean;
}

const NEAR_BOTTOM_THRESHOLD_PX = 96;

export function EditRoomTranscriptPanel({
  stepResult,
  stepEvents,
  artifactJson,
  artifactError,
  artifactLoading,
}: EditRoomTranscriptPanelProps) {
  const turns = useMemo(() => {
    const byTurnIndex = new Map<number, WorkflowStepChatTurn>();
    for (const event of stepEvents) {
      if (event.type !== 'step.chat-turn') continue;
      const turn = parseChatTurn(event);
      if (!turn) continue;
      // `stepEvents` is newest-first, so the first occurrence per turnIndex is already the
      // latest copy — dedupe defensively in case of at-least-once SSE redelivery.
      if (!byTurnIndex.has(turn.turnIndex)) {
        byTurnIndex.set(turn.turnIndex, turn);
      }
    }
    return Array.from(byTurnIndex.values()).sort((a, b) => a.turnIndex - b.turnIndex);
  }, [stepEvents]);

  const totalTurns = useMemo(
    () => turns.find((turn) => turn.totalTurns != null)?.totalTurns ?? null,
    [turns],
  );

  const isRunning = stepResult.status === 'Running';

  const viewportRef = useRef<HTMLDivElement>(null);
  const [stickToBottom, setStickToBottom] = useState(true);
  const [showFullTranscript, setShowFullTranscript] = useState(false);

  const handleScrollPositionChange = ({ y }: { x: number; y: number }) => {
    const viewport = viewportRef.current;
    if (!viewport) return;
    const distanceFromBottom = viewport.scrollHeight - viewport.clientHeight - y;
    setStickToBottom(distanceFromBottom < NEAR_BOTTOM_THRESHOLD_PX);
  };

  useEffect(() => {
    if (!stickToBottom) return;
    const viewport = viewportRef.current;
    if (!viewport) return;
    viewport.scrollTo({ top: viewport.scrollHeight, behavior: 'smooth' });
    // Re-run whenever the visible turn count or the trailing "thinking" placeholder changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [turns.length, isRunning, stickToBottom]);

  return (
    <Stack gap="sm">
      <style>{`
        .editroom-thinking-dots { display: inline-flex; align-items: center; gap: 3px; }
        .editroom-thinking-dots span {
          width: 4px;
          height: 4px;
          border-radius: 50%;
          background: var(--mantine-color-gray-5);
          animation: editroom-thinking-bounce 1.2s infinite ease-in-out;
        }
        .editroom-thinking-dots span:nth-of-type(2) { animation-delay: 0.2s; }
        .editroom-thinking-dots span:nth-of-type(3) { animation-delay: 0.4s; }
        @keyframes editroom-thinking-bounce {
          0%, 80%, 100% { opacity: 0.25; transform: translateY(0); }
          40% { opacity: 1; transform: translateY(-2px); }
        }
      `}</style>

      <Group justify="space-between" wrap="wrap">
        <Group gap={6}>
          <IconUsers size={16} />
          <Text size="sm" fw={600}>Edit Room Discussion</Text>
        </Group>
        <Badge size="sm" variant="light" color="teal">
          {totalTurns ? `${turns.length} of ${totalTurns} turns` : `${turns.length} turn${turns.length === 1 ? '' : 's'}`}
        </Badge>
      </Group>

      {turns.length === 0 && !isRunning && (
        <Alert icon={<IconAlertCircle size={14} />} color="gray" variant="light">
          No room discussion recorded for this step yet.
        </Alert>
      )}

      <ScrollArea.Autosize
        mah={420}
        viewportRef={viewportRef}
        onScrollPositionChange={handleScrollPositionChange}
      >
        <Stack gap="xs" py={2}>
          {turns.map((turn) => (
            <ChatBubble key={turn.turnIndex} turn={turn} />
          ))}
          {isRunning && <ThinkingBubble />}
        </Stack>
      </ScrollArea.Autosize>

      {stepResult.artifactStorageKey && (
        <div>
          <Group
            gap={6}
            onClick={() => setShowFullTranscript((current) => !current)}
            style={{ cursor: 'pointer' }}
          >
            <Text size="xs" c="dimmed" td="underline">
              {showFullTranscript ? 'Hide full transcript artifact' : 'View full transcript artifact'}
            </Text>
          </Group>
          {showFullTranscript && (
            <Stack gap={4} mt={4}>
              {artifactLoading && <Text size="xs" c="dimmed">Loading full transcript…</Text>}
              {artifactError && (
                <Alert icon={<IconAlertCircle size={14} />} color="yellow" variant="light">
                  Could not load full transcript: {artifactError}
                </Alert>
              )}
              {artifactJson && <JsonViewer label="Full room transcript" value={artifactJson} />}
            </Stack>
          )}
        </div>
      )}
    </Stack>
  );
}
