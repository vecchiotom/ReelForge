'use client';

import { use, useEffect, useState, useCallback, useMemo } from 'react';
import { Stack, Card, Group, Text, Badge, Loader, Center, Progress, Timeline, Paper, Alert, Modal, Divider, ScrollArea, Button, SimpleGrid } from '@mantine/core';
import { IconPlayerPlay, IconCheck, IconX, IconClock, IconAlertCircle, IconPlayerStop, IconBolt, IconActivity, IconTool, IconBrain } from '@tabler/icons-react';
import { motion } from 'framer-motion';
import {
  ReactFlow,
  Background,
  BackgroundVariant,
  Controls,
  Node,
  Edge,
  ReactFlowProvider,
  useNodesState,
  useEdgesState,
  MarkerType,
  type NodeMouseHandler,
} from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import { PageHeader } from '@/components/shared/PageHeader';
import { apiFetch } from '@/lib/api/client';
import { getOutputVideoUrl } from '@/lib/api/outputs';
import { getStepResultArtifact } from '@/lib/api/step-result-artifacts';
import { getExecution, stopExecution } from '@/lib/api/executions';
import type { WorkflowExecution, WorkflowDefinition, WorkflowStepResult } from '@/lib/types/workflow';
import { formatDate, formatDurationLong } from '@/lib/utils/format';
import { JsonViewer } from '@/components/workflows/JsonViewer';
import { useExecutionStream, type ExecutionStreamEvent } from '@/lib/hooks/use-execution-stream';

function isTerminalExecutionStatus(status: WorkflowExecution['status'] | undefined): boolean {
  return status === 'Passed' || status === 'Failed' || status === 'Cancelled';
}

function getPayloadString(payload: Record<string, unknown>, ...keys: string[]): string {
  for (const key of keys) {
    const value = payload[key];
    if (typeof value === 'string' && value.length > 0) {
      return value;
    }
  }

  return '';
}

function getPayloadNumber(payload: Record<string, unknown>, ...keys: string[]): number {
  for (const key of keys) {
    const value = payload[key];
    if (typeof value === 'number' && Number.isFinite(value)) {
      return value;
    }
  }

  return 0;
}

function getEventBadgeColor(eventType: string): string {
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
  if (event.type === 'step.tool-called') return 'Tool call';
  if (event.type === 'step.reasoning') return 'Model reasoning';
  return 'Step completed';
}

function getEventIcon(eventType: ExecutionStreamEvent['type']) {
  if (eventType === 'execution.completed') return <IconCheck size={12} />;
  if (eventType === 'execution.failed') return <IconX size={12} />;
  if (eventType === 'execution.running') return <IconActivity size={12} />;
  if (eventType === 'step.started') return <IconClock size={12} />;
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

function ExecutionEventCard({ event }: { event: ExecutionStreamEvent }) {
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

function ExecutionDetailPageInner({ params }: { params: Promise<{ id: string; workflowId: string; executionId: string }> }) {
  const { id: projectId, workflowId, executionId } = use(params);

  const [execution, setExecution] = useState<WorkflowExecution | null>(null);
  const [workflow, setWorkflow] = useState<WorkflowDefinition | null>(null);
  const [loading, setLoading] = useState(true);
  const [nodes, setNodes, onNodesChange] = useNodesState<Node>([]);
  const [edges, setEdges, onEdgesChange] = useEdgesState<Edge>([]);
  const [selectedStepResult, setSelectedStepResult] = useState<WorkflowStepResult | null>(null);
  const [modalOpened, setModalOpened] = useState(false);
  const [artifactJson, setArtifactJson] = useState<string | null>(null);
  const [artifactError, setArtifactError] = useState<string | null>(null);
  const [artifactLoading, setArtifactLoading] = useState(false);

  const streamEnabled = !!execution && !!workflow && !isTerminalExecutionStatus(execution.status);
  const {
    events,
    connectionState,
    lastError,
    lastEventAt,
    metrics,
  } = useExecutionStream({
    projectId,
    workflowId,
    executionId,
    enabled: streamEnabled,
  });

  // Initialize flow visualization
  const initializeFlow = useCallback((wf: WorkflowDefinition, exec: WorkflowExecution) => {
    const newNodes: Node[] = [];
    const newEdges: Edge[] = [];

    wf.steps.forEach((step, index) => {
      const stepResult = exec.stepResults.find((r) => r.workflowStepId === step.id);
      const status = stepResult?.status || 'Pending';
      const stepName = step.label || `Step ${step.stepOrder}`;

      const color =
        status === 'Completed' ? 'var(--mantine-color-green-6)'
          : status === 'Running' ? 'var(--mantine-color-blue-6)'
            : status === 'Failed' ? 'var(--mantine-color-red-6)'
              : status === 'Skipped' ? 'var(--mantine-color-yellow-6)'
                : 'var(--mantine-color-gray-6)';

      newNodes.push({
        id: step.id,
        type: 'default',
        position: { x: 250, y: index * 150 + 50 },
        data: {
          label: (
            <div style={{ padding: 10, minWidth: 260, cursor: 'pointer' }}>
              <Group gap="xs" mb={6} justify="space-between">
                <Badge size="xs" variant="filled" style={{ background: color }}>
                  {status}
                </Badge>
                <Badge size="xs" variant="light" color="gray">
                  #{step.stepOrder} {step.stepType ?? 'Agent'}
                </Badge>
              </Group>
              <Text size="xs" fw={700} mb={6} lineClamp={2}>{stepName}</Text>
              {stepResult && (
                <Group gap="xs" wrap="nowrap">
                  <Badge size="xs" variant="dot" color="indigo">
                    {Math.round(stepResult.durationMs)}ms
                  </Badge>
                  <Badge size="xs" variant="dot" color="violet">
                    {stepResult.tokensUsed.toLocaleString()} tok
                  </Badge>
                  {stepResult.iterationNumber != null && (
                    <Badge size="xs" variant="dot" color="cyan">iter {stepResult.iterationNumber}</Badge>
                  )}
                </Group>
              )}
            </div>
          ),
          stepResult, // Store the step result in node data
        },
        style: {
          border: `2px solid ${color}`,
          borderRadius: 14,
          background:
            status === 'Running'
              ? 'linear-gradient(135deg, var(--mantine-color-dark-7), var(--mantine-color-dark-6))'
              : 'linear-gradient(135deg, var(--mantine-color-dark-8), var(--mantine-color-dark-7))',
          color: 'var(--mantine-color-gray-0)',
          boxShadow:
            status === 'Running'
              ? `0 0 0 1px ${color}, 0 0 28px ${color}`
              : `0 0 0 1px ${color}33`,
          animation: status === 'Running' ? 'pulse 2s infinite' : 'none',
        },
      });

      if (index > 0) {
        newEdges.push({
          id: `e${wf.steps[index - 1].id}-${step.id}`,
          source: wf.steps[index - 1].id,
          target: step.id,
          type: 'smoothstep',
          animated: status === 'Running',
          markerEnd: {
            type: MarkerType.ArrowClosed,
            color,
          },
          style: {
            stroke: color,
            strokeWidth: 2,
          },
        });
      }
    });

    setNodes(newNodes);
    setEdges(newEdges);
  }, [setNodes, setEdges]);

  // Handle node click
  const onNodeClick: NodeMouseHandler = useCallback((_event, node) => {
    const stepResult = node.data.stepResult as WorkflowStepResult | undefined;
    if (stepResult) {
      setSelectedStepResult(stepResult);
      setModalOpened(true);
    }
  }, []);

  const selectedStepEvents = useMemo(() => {
    if (!selectedStepResult) {
      return [];
    }

    return events.filter((event) => {
      const eventStepResultId = getPayloadString(event.payload, 'stepResultId', 'StepResultId');
      const eventStepId = getPayloadString(event.payload, 'stepId', 'StepId', 'workflowStepId', 'WorkflowStepId');

      return eventStepResultId === selectedStepResult.id || eventStepId === selectedStepResult.workflowStepId;
    });
  }, [events, selectedStepResult]);

  const selectedStepLiveTokenMetrics = useMemo(() => {
    const completedStepEvent = selectedStepEvents.find((event) => event.type === 'step.completed');
    if (!completedStepEvent) {
      return { inputTokens: 0, outputTokens: 0 };
    }

    return {
      inputTokens: getPayloadNumber(completedStepEvent.payload, 'inputTokens', 'InputTokens'),
      outputTokens: getPayloadNumber(completedStepEvent.payload, 'outputTokens', 'OutputTokens'),
    };
  }, [selectedStepEvents]);

  // Fetch initial data
  useEffect(() => {
    const fetchData = async () => {
      try {
        const [execData, workflowData] = await Promise.all([
          // note: getExecution no longer requires workflowId
          getExecution(projectId, executionId),
          apiFetch<WorkflowDefinition>(`/api/v1/projects/${projectId}/workflows/${workflowId}`),
        ]);
        setExecution(execData);
        setWorkflow(workflowData);
        initializeFlow(workflowData, execData);
      } catch (error) {
        console.error('Failed to fetch execution:', error);
      } finally {
        setLoading(false);
      }
    };
    fetchData();
  }, [projectId, workflowId, executionId, initializeFlow]);

  // Refresh execution snapshot when new stream events arrive.
  useEffect(() => {
    if (!workflow || !lastEventAt) {
      return;
    }

    const syncExecution = async () => {
      try {
        const data = await getExecution(projectId, executionId);
        setExecution(data);
        initializeFlow(workflow, data);
      } catch (error) {
        console.error('Failed to sync execution from event stream:', error);
      }
    };

    void syncExecution();
  }, [lastEventAt, projectId, executionId, workflow, initializeFlow]);

  useEffect(() => {
    if (!execution || !selectedStepResult) {
      return;
    }

    const updatedSelection = execution.stepResults.find((stepResult) => stepResult.id === selectedStepResult.id);
    if (updatedSelection) {
      setSelectedStepResult(updatedSelection);
    }
  }, [execution, selectedStepResult]);

  // Fetch the step's large artifact (e.g. a VideoAnalyze analysis JSON or VideoCompile EDL) whenever
  // the selected step result carries one. Separate from `output`/`outputJson` — this is the
  // "Edit decision list" style panel backed by `artifactStorageKey`.
  // Depend on the stable id + artifact key rather than the whole `selectedStepResult` object: the
  // SSE sync effect above replaces `execution` (and thus this object's identity) on every event,
  // which previously re-triggered this fetch of a potentially large artifact even when neither the
  // selected step nor its artifact had actually changed (found by Copilot review).
  const selectedStepResultId = selectedStepResult?.id;
  const selectedArtifactStorageKey = selectedStepResult?.artifactStorageKey;

  useEffect(() => {
    setArtifactJson(null);
    setArtifactError(null);
    if (!selectedArtifactStorageKey || !selectedStepResultId) {
      return;
    }
    setArtifactLoading(true);
    getStepResultArtifact(projectId, selectedStepResultId)
      .then((data) => setArtifactJson(JSON.stringify(data)))
      .catch((err: unknown) => setArtifactError(err instanceof Error ? err.message : 'Failed to load artifact'))
      .finally(() => setArtifactLoading(false));
  }, [projectId, selectedStepResultId, selectedArtifactStorageKey]);

  if (loading) {
    return <Center h={400}><Loader size="lg" /></Center>;
  }

  if (!execution || !workflow) {
    return (
      <Alert icon={<IconAlertCircle size={16} />} title="Not Found" color="red">
        Execution not found
      </Alert>
    );
  }

  const progress = execution.stepResults.length > 0
    ? (execution.stepResults.filter((r) => r.status === 'Completed').length / workflow.steps.length) * 100
    : 0;

  const duration = execution.startedAt && execution.completedAt
    ? new Date(execution.completedAt).getTime() - new Date(execution.startedAt).getTime()
    : execution.startedAt
      ? Date.now() - new Date(execution.startedAt).getTime()
      : 0;

  return (
    <>
      <PageHeader
        title={`Execution ${executionId.slice(0, 8)}`}
        breadcrumbs={[
          { label: 'Projects', href: '/projects' },
          { label: 'Project', href: `/projects/${projectId}` },
          { label: workflow.name, href: `/projects/${projectId}/workflows/${workflowId}` },
          { label: 'Execution' },
        ]}
      />

      <Stack gap="lg">
        {/* Status Card */}
        <motion.div
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.4 }}
        >
          <Card
            withBorder
            shadow="sm"
            radius="lg"
            padding="lg"
            style={{
              background: 'linear-gradient(140deg, var(--mantine-color-dark-8), var(--mantine-color-violet-9))',
              borderColor: 'var(--mantine-color-violet-6)',
            }}
          >
            <Stack gap="md">
              <Group justify="space-between">
                <Group gap="sm">
                  <Badge
                    size="xl"
                    variant="gradient"
                    gradient={
                      execution.status === 'Passed'
                        ? { from: 'green', to: 'teal', deg: 90 }
                        : execution.status === 'Failed'
                          ? { from: 'red', to: 'orange', deg: 90 }
                          : execution.status === 'Cancelled'
                            ? { from: 'orange', to: 'red', deg: 90 }
                            : execution.status === 'Running'
                              ? { from: 'blue', to: 'cyan', deg: 90 }
                              : { from: 'gray', to: 'dark', deg: 90 }
                    }
                    leftSection={
                      execution.status === 'Passed' ? <IconCheck size={16} />
                        : execution.status === 'Failed' ? <IconX size={16} />
                          : execution.status === 'Running' ? <IconPlayerPlay size={16} />
                            : <IconClock size={16} />
                    }
                  >
                    {execution.status.toUpperCase()}
                  </Badge>
                  <Text size="sm" c="gray.3">
                    {duration > 0 ? formatDurationLong(duration) : 'Not started'}
                  </Text>
                </Group>

                <Group gap="xs">
                  <Badge
                    size="md"
                    variant="light"
                    color={
                      connectionState === 'connected'
                        ? 'green'
                        : connectionState === 'reconnecting'
                          ? 'yellow'
                          : connectionState === 'connecting'
                            ? 'blue'
                            : 'gray'
                    }
                    leftSection={<IconActivity size={12} />}
                  >
                    Stream {connectionState}
                  </Badge>
                  {execution.status === 'Running' && <Loader size="sm" color="blue" />}
                </Group>
                {execution.status === 'Running' && (
                  <Button
                    size="xs"
                    variant="outline"
                    color="red"
                    leftSection={<IconPlayerStop size={14} />}
                    onClick={async () => {
                      try {
                        await stopExecution(projectId, workflowId, executionId);
                        // refresh execution data immediately using root endpoint
                        const data = await getExecution(projectId, executionId);
                        setExecution(data);
                        initializeFlow(workflow, data);
                      } catch (err) {
                        console.error('stop failed', err);
                      }
                    }}
                  >
                    Stop
                  </Button>
                )}
              </Group>

              <Progress
                value={progress}
                size="xl"
                radius="xl"
                animated={execution.status === 'Running'}
                color={
                  execution.status === 'Passed' ? 'green'
                    : execution.status === 'Failed' ? 'red'
                      : execution.status === 'Cancelled' ? 'orange'
                        : 'blue'
                }
              />

              <Group grow>
                <Paper withBorder p="sm" radius="md">
                  <Text size="xs" c="dimmed">Steps Completed</Text>
                  <Text size="lg" fw={700}>
                    {execution.stepResults.filter((r) => r.status === 'Completed').length} / {workflow.steps.length}
                  </Text>
                </Paper>
                <Paper withBorder p="sm" radius="md">
                  <Text size="xs" c="dimmed">Iterations</Text>
                  <Text size="lg" fw={700}>{execution.iterationCount}</Text>
                </Paper>
                <Paper withBorder p="sm" radius="md">
                  <Text size="xs" c="dimmed">Started</Text>
                  <Text size="sm" fw={600}>
                    {execution.startedAt ? formatDate(execution.startedAt) : 'Pending'}
                  </Text>
                </Paper>
                <Paper withBorder p="sm" radius="md">
                  <Text size="xs" c="dimmed">Stream Events</Text>
                  <Text size="lg" fw={700}>{metrics.totalEvents}</Text>
                </Paper>
                <Paper withBorder p="sm" radius="md">
                  <Text size="xs" c="dimmed">Live Tokens (In / Out / Total)</Text>
                  <Group gap={6} wrap="nowrap">
                    <IconBolt size={14} />
                    <Text size="lg" fw={700}>
                      {metrics.totalInputTokens.toLocaleString()} / {metrics.totalOutputTokens.toLocaleString()} / {metrics.totalTokens.toLocaleString()}
                    </Text>
                  </Group>
                </Paper>
              </Group>

              {lastError && (
                <Alert icon={<IconAlertCircle size={14} />} color="yellow" variant="light">
                  {lastError}
                </Alert>
              )}
            </Stack>
          </Card>
        </motion.div>

        {/* Flow Visualization */}
        <motion.div
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.4, delay: 0.2 }}
        >
          <SimpleGrid cols={{ base: 1, xl: 2 }} spacing="lg">
            <Card withBorder shadow="sm" radius="lg" padding="lg">
              <Group justify="space-between" mb="md">
                <Text fw={600}>Workflow Flow</Text>
                <Badge size="sm" variant="light" color="indigo">
                  Avg {metrics.averageDurationMs}ms/step
                </Badge>
              </Group>
              <div style={{ height: 500, border: '1px solid var(--mantine-color-gray-4)', borderRadius: 12, overflow: 'hidden' }}>
                <ReactFlow
                  nodes={nodes}
                  edges={edges}
                  onNodesChange={onNodesChange}
                  onEdgesChange={onEdgesChange}
                  onNodeClick={onNodeClick}
                  fitView
                  nodesDraggable={false}
                  nodesConnectable={false}
                  elementsSelectable={true}
                >
                  <Background variant={BackgroundVariant.Dots} gap={16} size={1} />
                  <Controls showInteractive={false} />
                </ReactFlow>
              </div>
            </Card>

            <Card withBorder shadow="sm" radius="lg" padding="lg">
              <Text fw={600} mb="md">Step Bubble Insight</Text>
              {!selectedStepResult ? (
                <Alert icon={<IconPlayerPlay size={14} />} color="blue" variant="light">
                  Select any step bubble to inspect live details.
                </Alert>
              ) : (
                <Stack gap="md">
                  <Group justify="space-between">
                    <Badge
                      size="lg"
                      variant="light"
                      color={
                        selectedStepResult.status === 'Completed'
                          ? 'green'
                          : selectedStepResult.status === 'Running'
                            ? 'blue'
                            : selectedStepResult.status === 'Failed'
                              ? 'red'
                              : 'gray'
                      }
                    >
                      {selectedStepResult.status ?? 'Pending'}
                    </Badge>
                    <Text size="sm" c="dimmed">{formatDate(selectedStepResult.executedAt)}</Text>
                  </Group>
                  <Group grow>
                    <Paper withBorder p="sm" radius="md">
                      <Text size="xs" c="dimmed">Duration</Text>
                      <Text fw={700}>{Math.round(selectedStepResult.durationMs)}ms</Text>
                    </Paper>
                    <Paper withBorder p="sm" radius="md">
                        <Text size="xs" c="dimmed">Tokens (In / Out / Total)</Text>
                      <Text fw={700}>{selectedStepResult.tokensUsed.toLocaleString()}</Text>
                    </Paper>
                  </Group>
                    <Group grow>
                      <Paper withBorder p="sm" radius="md">
                        <Text size="xs" c="dimmed">Input Tokens</Text>
                        <Text fw={700}>{selectedStepLiveTokenMetrics.inputTokens.toLocaleString()}</Text>
                      </Paper>
                      <Paper withBorder p="sm" radius="md">
                        <Text size="xs" c="dimmed">Output Tokens</Text>
                        <Text fw={700}>{selectedStepLiveTokenMetrics.outputTokens.toLocaleString()}</Text>
                      </Paper>
                    </Group>
                    <Card withBorder padding="sm" radius="md">
                      <Text size="xs" fw={600} mb="xs">Live Step Updates</Text>
                      <ScrollArea.Autosize mah={180}>
                        <Stack gap="xs">
                          {selectedStepEvents.length === 0 ? (
                            <Text size="xs" c="dimmed">Waiting for step events...</Text>
                          ) : (
                            selectedStepEvents.map((event, index) => (
                              <ExecutionEventCard key={`${event.id}-${index}`} event={event} />
                            ))
                          )}
                        </Stack>
                      </ScrollArea.Autosize>
                    </Card>
                  {selectedStepResult.outputStorageKey && (
                    <video
                      controls
                      style={{ width: '100%', borderRadius: 8 }}
                      src={getOutputVideoUrl(projectId, selectedStepResult.id)}
                    />
                  )}
                  <ScrollArea.Autosize mah={220}>
                    <Text size="sm" style={{ whiteSpace: 'pre-wrap' }}>
                      {selectedStepResult.output || 'No output available'}
                    </Text>
                  </ScrollArea.Autosize>
                </Stack>
              )}
            </Card>
          </SimpleGrid>
        </motion.div>

        {/* Event Timeline */}
        <motion.div
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.4, delay: 0.4 }}
        >
          <Card withBorder shadow="sm" radius="lg" padding="lg">
            <Group justify="space-between" mb="md">
              <Text fw={600}>Real-Time Events</Text>
              <Badge
                size="sm"
                variant="light"
                color={
                  connectionState === 'connected'
                    ? 'green'
                    : connectionState === 'reconnecting'
                      ? 'yellow'
                      : connectionState === 'connecting'
                        ? 'blue'
                        : 'gray'
                }
              >
                Stream: {connectionState}
              </Badge>
            </Group>
            {lastError && (
              <Alert icon={<IconAlertCircle size={14} />} color="yellow" variant="light" mb="md">
                {lastError}
              </Alert>
            )}
            {events.length === 0 ? (
              <Text size="sm" c="dimmed">Waiting for events...</Text>
            ) : (
              <Timeline active={events.length - 1} bulletSize={24} lineWidth={2}>
                {events.map((event, i) => (
                  <Timeline.Item
                    key={i}
                    bullet={getEventIcon(event.type)}
                    title={
                      <Group gap="xs">
                        <Badge size="sm" color={getEventBadgeColor(event.type)}>{event.type}</Badge>
                        <Text size="xs" c="dimmed">{new Date(event.timestamp).toLocaleTimeString()}</Text>
                      </Group>
                    }
                  >
                    <ExecutionEventCard event={event} />
                  </Timeline.Item>
                ))}
              </Timeline>
            )}
          </Card>
        </motion.div>
      </Stack>

      {/* Step Detail Modal */}
      <Modal
        opened={modalOpened}
        onClose={() => setModalOpened(false)}
        title={<Text fw={700} size="lg">Step Result Details</Text>}
        size="xl"
        scrollAreaComponent={ScrollArea.Autosize}
      >
        {selectedStepResult && (
          <Stack gap="md">
            {/* Status and Metadata */}
            <Card withBorder padding="md" radius="md">
              <Stack gap="sm">
                <Group justify="space-between">
                  <Text size="sm" fw={600}>Status</Text>
                  <Badge
                    size="lg"
                    variant="filled"
                    color={
                      selectedStepResult.status === 'Completed' ? 'green'
                        : selectedStepResult.status === 'Running' ? 'blue'
                          : selectedStepResult.status === 'Failed' ? 'red'
                            : 'gray'
                    }
                  >
                    {selectedStepResult.status || 'Unknown'}
                  </Badge>
                </Group>
                <Divider />
                <Group justify="space-between">
                  <Text size="sm" c="dimmed">Duration</Text>
                  <Text size="sm" fw={500}>{Math.round(selectedStepResult.durationMs)}ms</Text>
                </Group>
                <Group justify="space-between">
                  <Text size="sm" c="dimmed">Tokens Used</Text>
                  <Text size="sm" fw={500}>{selectedStepResult.tokensUsed.toLocaleString()}</Text>
                </Group>
                <Group justify="space-between">
                  <Text size="sm" c="dimmed">Input Tokens (live)</Text>
                  <Text size="sm" fw={500}>{selectedStepLiveTokenMetrics.inputTokens.toLocaleString()}</Text>
                </Group>
                <Group justify="space-between">
                  <Text size="sm" c="dimmed">Output Tokens (live)</Text>
                  <Text size="sm" fw={500}>{selectedStepLiveTokenMetrics.outputTokens.toLocaleString()}</Text>
                </Group>
                {selectedStepResult.iterationNumber !== null && selectedStepResult.iterationNumber !== undefined && (
                  <Group justify="space-between">
                    <Text size="sm" c="dimmed">Iteration</Text>
                    <Text size="sm" fw={500}>#{selectedStepResult.iterationNumber}</Text>
                  </Group>
                )}
                <Group justify="space-between">
                  <Text size="sm" c="dimmed">Executed At</Text>
                  <Text size="sm" fw={500}>{formatDate(selectedStepResult.executedAt)}</Text>
                </Group>
                {selectedStepResult.completedAt && (
                  <Group justify="space-between">
                    <Text size="sm" c="dimmed">Completed At</Text>
                    <Text size="sm" fw={500}>{formatDate(selectedStepResult.completedAt)}</Text>
                  </Group>
                )}
              </Stack>
            </Card>

            {/* Error Details (if any) */}
            {selectedStepResult.errorDetails && (
              <Alert icon={<IconAlertCircle size={16} />} title="Error Details" color="red" variant="light">
                <Text size="sm" style={{ whiteSpace: 'pre-wrap' }}>{selectedStepResult.errorDetails}</Text>
              </Alert>
            )}

            {/* Output Video */}
            {selectedStepResult.outputStorageKey && (
              <Card withBorder padding="md" radius="md">
                <Text size="sm" fw={600} mb="xs">Output Video</Text>
                <Divider mb="sm" />
                <video
                  controls
                  style={{ width: '100%', borderRadius: 8 }}
                  src={getOutputVideoUrl(projectId, selectedStepResult.id)}
                />
              </Card>
            )}

            {/* Edit decision / artifact panel — VideoAnalyze's full analysis JSON or VideoCompile's EDL */}
            {selectedStepResult.artifactStorageKey && (
              <Card withBorder padding="md" radius="md">
                <Text size="sm" fw={600} mb="xs">Edit Decision Artifact</Text>
                <Divider mb="sm" />
                {artifactLoading && <Text size="sm" c="dimmed">Loading artifact...</Text>}
                {artifactError && (
                  <Alert icon={<IconAlertCircle size={14} />} color="yellow" variant="light">
                    Could not load artifact: {artifactError}
                  </Alert>
                )}
                {artifactJson && <JsonViewer label="Artifact JSON" value={artifactJson} />}
              </Card>
            )}

            {/* Output */}
            <Card withBorder padding="md" radius="md">
              <Text size="sm" fw={600} mb="xs">Output</Text>
              <Divider mb="sm" />
              <ScrollArea.Autosize mah={300}>
                <Text size="sm" style={{ whiteSpace: 'pre-wrap' }}>
                  {selectedStepResult.output || 'No output available'}
                </Text>
              </ScrollArea.Autosize>
            </Card>

            <Card withBorder padding="md" radius="md">
              <Text size="sm" fw={600} mb="xs">Live Event Updates</Text>
              <Divider mb="sm" />
              <ScrollArea.Autosize mah={260}>
                <Stack gap="xs">
                  {selectedStepEvents.length === 0 ? (
                    <Text size="sm" c="dimmed">No live updates yet for this step.</Text>
                  ) : (
                    selectedStepEvents.map((event, index) => (
                      <ExecutionEventCard key={`${event.id}-modal-${index}`} event={event} />
                    ))
                  )}
                </Stack>
              </ScrollArea.Autosize>
            </Card>

            {/* Input JSON */}
            {selectedStepResult.inputJson && (
              <Card withBorder padding="md" radius="md">
                <JsonViewer label="Input JSON" value={selectedStepResult.inputJson} />
              </Card>
            )}

            {/* Output JSON */}
            {selectedStepResult.outputJson && (
              <Card withBorder padding="md" radius="md">
                <JsonViewer label="Output JSON" value={selectedStepResult.outputJson} />
              </Card>
            )}
          </Stack>
        )}
      </Modal>

      <style jsx global>{`
        @keyframes pulse {
          0%, 100% {
            box-shadow: 0 0 0 0 var(--mantine-color-blue-6);
          }
          50% {
            box-shadow: 0 0 0 10px transparent;
          }
        }
      `}</style>
    </>
  );
}

export default function ExecutionDetailPage(props: { params: Promise<{ id: string; workflowId: string; executionId: string }> }) {
  return (
    <ReactFlowProvider>
      <ExecutionDetailPageInner {...props} />
    </ReactFlowProvider>
  );
}
