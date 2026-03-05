export type WorkflowEventType = 'execution.completed' | 'execution.failed' | 'step.completed';

export interface WorkflowStatsSnapshot {
  queued: number;
  active: number;
  completed: number;
  failed: number;
  total: number;
}

export type WorkflowEventPayload = Record<string, unknown>;

export interface WorkflowStreamEvent {
  id: string;
  type: WorkflowEventType;
  executionId: string;
  timestamp: string;
  payload: WorkflowEventPayload;
}

export interface ExecutionRuntimeSnapshot {
  executionId: string;
  status: 'running' | 'completed' | 'failed';
  lastEventType: WorkflowEventType;
  lastSeenAt: string;
  correlationId: string | null;
  lastDurationMs: number;
  lastTokensUsed: number;
  lastStepStatus: string | null;
  errorMessage: string | null;
}

export interface PacketAnimation {
  id: string;
  lane: 'ingress' | 'execution' | 'egress';
  delayMs: number;
  tone: 'queue' | 'step' | 'success' | 'failed';
}
