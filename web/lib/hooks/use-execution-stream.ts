'use client';

import { useEffect, useMemo, useRef, useState } from 'react';

export type ExecutionStreamConnectionState = 'connecting' | 'connected' | 'reconnecting' | 'closed';

export interface ExecutionStreamEvent {
  id: string;
  type: 'step.completed' | 'execution.completed' | 'execution.failed';
  executionId: string;
  timestamp: string;
  payload: Record<string, unknown>;
}

interface IncomingExecutionEvent {
  type?: string;
  executionId?: string;
  timestamp?: string;
  data?: unknown;
}

function normalizePayload(data: unknown): Record<string, unknown> {
  if (typeof data === 'object' && data !== null) {
    return data as Record<string, unknown>;
  }

  return {};
}

function normalizeEvent(type: ExecutionStreamEvent['type'], raw: IncomingExecutionEvent): ExecutionStreamEvent {
  return {
    id: `${Date.now()}-${Math.random().toString(16).slice(2)}`,
    type,
    executionId: raw.executionId ?? 'unknown',
    timestamp: raw.timestamp ?? new Date().toISOString(),
    payload: normalizePayload(raw.data),
  };
}

interface UseExecutionStreamOptions {
  projectId: string;
  workflowId: string;
  executionId: string;
  enabled: boolean;
}

const EVENT_LIMIT = 150;
const RECONNECT_BASE_MS = 1000;
const RECONNECT_MAX_MS = 8000;

export function useExecutionStream({ projectId, workflowId, executionId, enabled }: UseExecutionStreamOptions) {
  const [connectionState, setConnectionState] = useState<ExecutionStreamConnectionState>('connecting');
  const [lastError, setLastError] = useState<string | null>(null);
  const [events, setEvents] = useState<ExecutionStreamEvent[]>([]);
  const [lastEventAt, setLastEventAt] = useState<string | null>(null);
  const attemptRef = useRef(0);

  useEffect(() => {
    if (!enabled) {
      setConnectionState('closed');
      return;
    }

    let source: EventSource | null = null;
    let reconnectTimeout: ReturnType<typeof setTimeout> | null = null;
    let cancelled = false;

    const connect = () => {
      if (cancelled) {
        return;
      }

      setConnectionState((current) => (current === 'connected' ? 'reconnecting' : 'connecting'));

      source = new EventSource(`/api/v1/projects/${projectId}/workflows/${workflowId}/executions/${executionId}/events`);

      source.addEventListener('connected', () => {
        attemptRef.current = 0;
        setConnectionState('connected');
        setLastError(null);
      });

      const onEvent = (eventType: ExecutionStreamEvent['type']) => (event: Event) => {
        try {
          const parsed = JSON.parse((event as MessageEvent<string>).data) as IncomingExecutionEvent;
          const normalized = normalizeEvent(eventType, parsed);

          setEvents((current) => [normalized, ...current].slice(0, EVENT_LIMIT));
          setLastEventAt(normalized.timestamp);
          setLastError(null);
        } catch {
          setLastError(`Failed to parse ${eventType} event payload.`);
        }
      };

      source.addEventListener('step.completed', onEvent('step.completed'));
      source.addEventListener('execution.completed', onEvent('execution.completed'));
      source.addEventListener('execution.failed', onEvent('execution.failed'));

      source.onerror = () => {
        if (cancelled) {
          return;
        }

        setConnectionState('reconnecting');
        setLastError('Realtime stream interrupted. Reconnecting...');

        if (source) {
          source.close();
          source = null;
        }

        const nextAttempt = attemptRef.current + 1;
        attemptRef.current = nextAttempt;
        const nextDelay = Math.min(RECONNECT_BASE_MS * 2 ** (nextAttempt - 1), RECONNECT_MAX_MS);
        reconnectTimeout = setTimeout(connect, nextDelay);
      };
    };

    connect();

    return () => {
      cancelled = true;
      if (reconnectTimeout) {
        clearTimeout(reconnectTimeout);
      }
      if (source) {
        source.close();
      }
      setConnectionState('closed');
    };
  }, [projectId, workflowId, executionId, enabled]);

  const metrics = useMemo(() => {
    const stepEvents = events.filter((event) => event.type === 'step.completed');

    const totalTokens = stepEvents.reduce((sum, event) => {
      const tokens = event.payload.tokensUsed;
      return sum + (typeof tokens === 'number' && Number.isFinite(tokens) ? tokens : 0);
    }, 0);

    const averageDurationMs =
      stepEvents.length > 0
        ? Math.round(
            stepEvents.reduce((sum, event) => {
              const duration = event.payload.durationMs;
              return sum + (typeof duration === 'number' && Number.isFinite(duration) ? duration : 0);
            }, 0) / stepEvents.length,
          )
        : 0;

    return {
      totalEvents: events.length,
      stepEventCount: stepEvents.length,
      totalTokens,
      averageDurationMs,
    };
  }, [events]);

  return {
    events,
    connectionState,
    lastError,
    lastEventAt,
    metrics,
  };
}
