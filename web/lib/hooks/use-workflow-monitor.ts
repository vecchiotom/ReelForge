'use client';

import { useEffect, useMemo, useState } from 'react';
import useSWR from 'swr';
import { fetcher } from '../api/client';
import type { AdminUser } from '../types/admin';
import type {
  ExecutionRuntimeSnapshot,
  PacketAnimation,
  WorkflowEventPayload,
  WorkflowEventType,
  WorkflowStatsSnapshot,
  WorkflowStreamEvent,
} from '../types/workflow-monitor';

const HISTORY_LIMIT = 220;
const EVENTS_PER_MINUTE_WINDOW_MS = 60_000;
const ACTIVE_EXECUTION_WINDOW_MS = 2 * 60_000;
const PARALLEL_AGENT_WINDOW_MS = 25_000;

interface IncomingWorkflowEvent {
  type?: string;
  executionId?: string;
  timestamp?: string;
  data?: unknown;
}

function parsePayload(data: unknown): WorkflowEventPayload {
  if (typeof data === 'object' && data !== null) {
    return data as WorkflowEventPayload;
  }
  return {};
}

function readNumber(payload: WorkflowEventPayload, key: string): number {
  const value = payload[key];
  return typeof value === 'number' && Number.isFinite(value) ? value : 0;
}

function readString(payload: WorkflowEventPayload, key: string): string | null {
  const value = payload[key];
  return typeof value === 'string' && value.length > 0 ? value : null;
}

function isWorkflowEventType(value: string): value is WorkflowEventType {
  return value === 'execution.completed' || value === 'execution.failed' || value === 'step.completed';
}

function toMillis(iso: string): number {
  const ts = Date.parse(iso);
  return Number.isNaN(ts) ? Date.now() : ts;
}

function toEvent(feedEventType: WorkflowEventType, raw: IncomingWorkflowEvent): WorkflowStreamEvent {
  const rawType = raw.type ?? '';
  const type: WorkflowEventType = isWorkflowEventType(rawType) ? rawType : feedEventType;

  return {
    id: `${Date.now()}-${Math.random().toString(16).slice(2)}`,
    type,
    executionId: raw.executionId ?? 'unknown',
    timestamp: raw.timestamp ?? new Date().toISOString(),
    payload: parsePayload(raw.data),
  };
}

function asExecutionMap(events: WorkflowStreamEvent[]): Map<string, ExecutionRuntimeSnapshot> {
  const map = new Map<string, ExecutionRuntimeSnapshot>();

  for (const event of events) {
    const existing = map.get(event.executionId);
    const durationMs = readNumber(event.payload, 'durationMs');
    const tokensUsed = readNumber(event.payload, 'tokensUsed');

    const next: ExecutionRuntimeSnapshot = {
      executionId: event.executionId,
      status:
        event.type === 'execution.completed'
          ? 'completed'
          : event.type === 'execution.failed'
            ? 'failed'
            : existing?.status ?? 'running',
      lastEventType: event.type,
      lastSeenAt: event.timestamp,
      correlationId: readString(event.payload, 'correlationId') ?? existing?.correlationId ?? null,
      lastDurationMs: durationMs || existing?.lastDurationMs || 0,
      lastTokensUsed: tokensUsed || existing?.lastTokensUsed || 0,
      lastStepStatus: readString(event.payload, 'stepStatus') ?? existing?.lastStepStatus ?? null,
      errorMessage: readString(event.payload, 'errorMessage') ?? existing?.errorMessage ?? null,
    };

    map.set(event.executionId, next);
  }

  return map;
}

export function useWorkflowMonitor() {
  const [connectionState, setConnectionState] = useState<'connecting' | 'connected' | 'reconnecting'>('connecting');
  const [lastError, setLastError] = useState<string | null>(null);
  const [events, setEvents] = useState<WorkflowStreamEvent[]>([]);
  const [lastEventAt, setLastEventAt] = useState<string | null>(null);

  const usersQuery = useSWR<AdminUser[]>('/api/v1/admin/users', fetcher, {
    refreshInterval: 60_000,
  });

  const statsQuery = useSWR<WorkflowStatsSnapshot>('/api/v1/workflows/stats', fetcher, {
    refreshInterval: 4_000,
  });

  useEffect(() => {
    const source = new EventSource('/api/v1/workflows/events');

    const upsertEvent = (feedEventType: WorkflowEventType) => (event: Event) => {
      try {
        const parsed = JSON.parse((event as MessageEvent<string>).data) as IncomingWorkflowEvent;
        const normalized = toEvent(feedEventType, parsed);
        setEvents((current) => [normalized, ...current].slice(0, HISTORY_LIMIT));
        setLastEventAt(normalized.timestamp);
        setConnectionState('connected');
        setLastError(null);
      } catch {
        setLastError('Failed to parse a workflow event payload.');
      }
    };

    const onConnected = () => {
      setConnectionState('connected');
      setLastError(null);
    };

    const onError = () => {
      setConnectionState('reconnecting');
      setLastError('Event stream interrupted. Reconnecting...');
    };

    source.addEventListener('connected', onConnected);
    source.addEventListener('execution.completed', upsertEvent('execution.completed'));
    source.addEventListener('execution.failed', upsertEvent('execution.failed'));
    source.addEventListener('step.completed', upsertEvent('step.completed'));
    source.onerror = onError;

    return () => {
      source.close();
    };
  }, []);

  const derived = useMemo(() => {
    const now = Date.now();
    const minuteAgo = now - EVENTS_PER_MINUTE_WINDOW_MS;
    const activeAgo = now - ACTIVE_EXECUTION_WINDOW_MS;
    const parallelWindowAgo = now - PARALLEL_AGENT_WINDOW_MS;

    const eventsLastMinute = events.filter((event) => toMillis(event.timestamp) >= minuteAgo);
    const stepEventsLastMinute = eventsLastMinute.filter((event) => event.type === 'step.completed');

    const eventsPerMinute = eventsLastMinute.length;
    const tokenRatePerMinute = stepEventsLastMinute.reduce(
      (sum, event) => sum + readNumber(event.payload, 'tokensUsed'),
      0,
    );

    const averageStepDurationMs =
      stepEventsLastMinute.length > 0
        ? Math.round(
            stepEventsLastMinute.reduce(
              (sum, event) => sum + readNumber(event.payload, 'durationMs'),
              0,
            ) / stepEventsLastMinute.length,
          )
        : 0;

    const executionMap = asExecutionMap(events);
    const executionSnapshots = Array.from(executionMap.values());

    const activeExecutions = executionSnapshots
      .filter((snapshot) => snapshot.status === 'running' && toMillis(snapshot.lastSeenAt) >= activeAgo)
      .sort((a, b) => toMillis(b.lastSeenAt) - toMillis(a.lastSeenAt));

    const executionIdsInParallelWindow = new Set(
      events
        .filter((event) => event.type === 'step.completed' && toMillis(event.timestamp) >= parallelWindowAgo)
        .map((event) => event.executionId),
    );

    const parallelAgentsEstimated = Math.max(
      statsQuery.data?.active ?? 0,
      activeExecutions.length,
      executionIdsInParallelWindow.size,
    );

    const queuePackets = Array.from({ length: Math.min(statsQuery.data?.queued ?? 0, 10) }).map((_, index) => ({
      id: `queue-${index}`,
      lane: 'ingress' as const,
      delayMs: index * 180,
      tone: 'queue' as const,
    }));

    const streamPackets: PacketAnimation[] = events
      .slice(0, 26)
      .map((event, index) => ({
        id: event.id,
        lane:
          event.type === 'step.completed'
            ? ('execution' as const)
            : ('egress' as const),
        delayMs: index * 130,
        tone:
          event.type === 'execution.completed'
            ? ('success' as const)
            : event.type === 'execution.failed'
              ? ('failed' as const)
              : ('step' as const),
      }));

    const packets = [...queuePackets, ...streamPackets];

    return {
      eventsPerMinute,
      tokenRatePerMinute,
      averageStepDurationMs,
      activeExecutions,
      parallelAgentsEstimated,
      packets,
    };
  }, [events, statsQuery.data?.active, statsQuery.data?.queued]);

  const users = usersQuery.data ?? [];
  const userStats = {
    total: users.length,
    admins: users.filter((user) => user.isAdmin).length,
    mustRotatePassword: users.filter((user) => user.mustChangePassword).length,
  };

  return {
    users,
    userStats,
    workflowStats: statsQuery.data,
    events,
    lastEventAt,
    connectionState,
    lastError,
    isLoading: usersQuery.isLoading || statsQuery.isLoading,
    derived,
    refreshStats: statsQuery.mutate,
  };
}
