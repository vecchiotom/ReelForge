'use client';

import { use, useEffect, useState, useCallback, useMemo } from 'react';
import { Stack, Card, Group, Text, Badge, Loader, Center, Progress, Timeline, Paper, Alert, Drawer, Button, SimpleGrid, Tooltip } from '@mantine/core';
import { useMediaQuery } from '@mantine/hooks';
import { IconPlayerPlay, IconCheck, IconX, IconClock, IconAlertCircle, IconPlayerStop, IconBolt, IconActivity } from '@tabler/icons-react';
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
import { getStepResultArtifact } from '@/lib/api/step-result-artifacts';
import { getExecution, stopExecution } from '@/lib/api/executions';
import type { WorkflowExecution, WorkflowDefinition, WorkflowStepResult } from '@/lib/types/workflow';
import { formatDate, formatDurationLong } from '@/lib/utils/format';
import { StepResultPanel } from '@/components/workflows/StepResultPanel';
import {
  ExecutionEventCard,
  getEventBadgeColor,
  getEventIcon,
  getPayloadNumber,
  getPayloadString,
} from '@/components/workflows/ExecutionEventCard';
import { useExecutionStream } from '@/lib/hooks/use-execution-stream';
import { hydrateHistoricalStepEvents, mergeStepEvents } from '@/lib/utils/execution-event-hydration';

function isTerminalExecutionStatus(status: WorkflowExecution['status'] | undefined): boolean {
  return status === 'Passed' || status === 'Failed' || status === 'Cancelled';
}

interface StepProgressInfo {
  stage: string;
  percent: number | null;
}

function ExecutionDetailPageInner({ params }: { params: Promise<{ id: string; workflowId: string; executionId: string }> }) {
  const { id: projectId, workflowId, executionId } = use(params);

  const [execution, setExecution] = useState<WorkflowExecution | null>(null);
  const [workflow, setWorkflow] = useState<WorkflowDefinition | null>(null);
  const [loading, setLoading] = useState(true);
  const [nodes, setNodes, onNodesChange] = useNodesState<Node>([]);
  const [edges, setEdges, onEdgesChange] = useEdgesState<Edge>([]);
  const [selectedStepResult, setSelectedStepResult] = useState<WorkflowStepResult | null>(null);
  const [artifactJson, setArtifactJson] = useState<string | null>(null);
  const [artifactError, setArtifactError] = useState<string | null>(null);
  const [artifactLoading, setArtifactLoading] = useState(false);
  // Below `sm` (768px) the flow canvas and step panel stack, so a selected step's detail would
  // scroll off-screen; render it as a bottom Drawer there instead of a grid cell.
  const isDesktop = useMediaQuery('(min-width: 48em)');

  const streamEnabled = !!execution && !!workflow && !isTerminalExecutionStatus(execution.status);
  const {
    events,
    connectionState,
    lastError,
    metrics,
  } = useExecutionStream({
    projectId,
    workflowId,
    executionId,
    enabled: streamEnabled,
  });

  // Latest live stage/percent per step, from 'step.progress' events — these are ephemeral
  // (never persisted server-side, see WorkflowStepProgress's doc comment), so this map is the
  // ONLY source for them; a REST resync of `execution` can never carry this information.
  // `events` is newest-first (see useExecutionStream), so the first occurrence per stepId wins.
  const latestProgressByStepId = useMemo(() => {
    const map = new Map<string, StepProgressInfo>();
    for (const event of events) {
      if (event.type !== 'step.progress') continue;
      const stepId = getPayloadString(event.payload, 'stepId', 'StepId');
      if (!stepId || map.has(stepId)) continue;
      const stage = getPayloadString(event.payload, 'stage', 'Stage');
      const percent = getPayloadNumber(event.payload, 'percentComplete', 'PercentComplete');
      map.set(stepId, { stage, percent: percent > 0 ? percent : null });
    }
    return map;
  }, [events]);

  // Initialize flow visualization
  const initializeFlow = useCallback((
    wf: WorkflowDefinition,
    exec: WorkflowExecution,
    progressByStepId: Map<string, StepProgressInfo> = new Map(),
  ) => {
    const newNodes: Node[] = [];
    const newEdges: Edge[] = [];

    wf.steps.forEach((step, index) => {
      const stepResult = exec.stepResults.find((r) => r.workflowStepId === step.id);
      const status = stepResult?.status || 'Pending';
      const stepName = step.label || `Step ${step.stepOrder}`;
      const liveProgress = status === 'Running' ? progressByStepId.get(step.id) : undefined;

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
            <div style={{ padding: 10, width: '100%', cursor: 'pointer' }}>
              <Group gap="xs" mb={6} justify="space-between">
                <Badge size="xs" variant="filled" style={{ background: color }}>
                  {status}
                </Badge>
                <Badge size="xs" variant="light" color="gray">
                  #{step.stepOrder} {step.stepType ?? 'Agent'}
                </Badge>
              </Group>
              <Tooltip label={stepName} multiline maw={260} openDelay={300}>
                <Text size="xs" fw={700} mb={6} lineClamp={2}>{stepName}</Text>
              </Tooltip>
              {liveProgress && (
                <Group gap={4} mb={6} wrap="nowrap">
                  <Loader size={10} />
                  <Text size="xs" c="blue.4" lineClamp={1} style={{ wordBreak: 'break-word' }}>
                    {liveProgress.stage}
                    {liveProgress.percent != null ? ` (${Math.round(liveProgress.percent)}%)` : ''}
                  </Text>
                </Group>
              )}
              {stepResult && (
                <Group gap={4} wrap="wrap">
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
          width: 280,
          border: `2px solid ${color}`,
          borderRadius: 14,
          background:
            status === 'Running'
              ? 'linear-gradient(135deg, var(--mantine-color-dark-7), var(--mantine-color-dark-6))'
              : 'linear-gradient(135deg, var(--mantine-color-dark-8), var(--mantine-color-dark-7))',
          color: 'var(--mantine-color-gray-0)',
          boxShadow:
            stepResult && stepResult.id === selectedStepResult?.id
              ? `0 0 0 3px ${color}, 0 0 24px ${color}`
              : status === 'Running'
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
  }, [setNodes, setEdges, selectedStepResult]);

  // Handle node click
  const onNodeClick: NodeMouseHandler = useCallback((_event, node) => {
    const stepResult = node.data.stepResult as WorkflowStepResult | undefined;
    if (stepResult) {
      setSelectedStepResult(stepResult);
    }
  }, []);

  const selectedWorkflowStep = useMemo(() => {
    if (!workflow || !selectedStepResult) {
      return undefined;
    }
    return workflow.steps.find((step) => step.id === selectedStepResult.workflowStepId);
  }, [workflow, selectedStepResult]);

  const selectedStepType = selectedWorkflowStep?.stepType;

  // Live SSE events for the selected step only (unfiltered `events` also carries other steps' and
  // execution-level events — see the full-feed Timeline below, which intentionally stays SSE-only).
  const liveSelectedStepEvents = useMemo(() => {
    if (!selectedStepResult) {
      return [];
    }

    return events.filter((event) => {
      const eventStepResultId = getPayloadString(event.payload, 'stepResultId', 'StepResultId');
      const eventStepId = getPayloadString(event.payload, 'stepId', 'StepId', 'workflowStepId', 'WorkflowStepId');

      return eventStepResultId === selectedStepResult.id || eventStepId === selectedStepResult.workflowStepId;
    });
  }, [events, selectedStepResult]);

  // Historical reasoning/tool-call/chat-turn events hydrated from the step result's persisted
  // `toolCallsJson`/`reasoningJson`/`chatTranscriptJson` fields (present once the step has
  // completed) — see `hydrateHistoricalStepEvents`'s own comment for why this only ever arrives as
  // one full batch, unlike the live trickle above.
  const historicalSelectedStepEvents = useMemo(() => {
    if (!selectedStepResult) {
      return [];
    }

    return hydrateHistoricalStepEvents(selectedStepResult, {
      executionId,
      stepId: selectedStepResult.workflowStepId,
      stepResultId: selectedStepResult.id,
      stepOrder: selectedWorkflowStep?.stepOrder,
      stepLabel: selectedWorkflowStep?.label,
    });
  }, [selectedStepResult, selectedWorkflowStep, executionId]);

  // Historical events seeded first (authoritative once persisted), live events merged in and
  // deduplicated against them — see `mergeStepEvents` for the exact per-event-type dedupe strategy.
  const selectedStepEvents = useMemo(
    () => mergeStepEvents(historicalSelectedStepEvents, liveSelectedStepEvents),
    [historicalSelectedStepEvents, liveSelectedStepEvents],
  );

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
      } catch (error) {
        console.error('Failed to fetch execution:', error);
      } finally {
        setLoading(false);
      }
    };
    fetchData();
  }, [projectId, workflowId, executionId]);

  // 'step.progress' is ephemeral (never persisted server-side — see WorkflowStepProgress's doc
  // comment) and fires far more often than a real DB change, so a REST resync only on OTHER event
  // types; progress ticks repaint the diagram locally via latestProgressByStepId below instead.
  const lastNonProgressEventAt = useMemo(
    () => events.find((event) => event.type !== 'step.progress')?.timestamp ?? null,
    [events],
  );

  // Refresh execution snapshot when a non-progress stream event arrives.
  useEffect(() => {
    if (!workflow || !lastNonProgressEventAt) {
      return;
    }

    const syncExecution = async () => {
      try {
        const data = await getExecution(projectId, executionId);
        setExecution(data);
      } catch (error) {
        console.error('Failed to sync execution from event stream:', error);
      }
    };

    void syncExecution();
  }, [lastNonProgressEventAt, projectId, executionId, workflow]);

  // Single reactive repaint of the flow diagram — runs on the initial load, every REST resync
  // above, AND every live progress tick (cheap: local state only, no network call).
  useEffect(() => {
    if (!workflow || !execution) {
      return;
    }
    initializeFlow(workflow, execution, latestProgressByStepId);
  }, [workflow, execution, latestProgressByStepId, initializeFlow]);

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
                        // refresh execution data immediately using root endpoint; the reactive
                        // repaint effect picks this up automatically
                        const data = await getExecution(projectId, executionId);
                        setExecution(data);
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

            {/* Below `sm` this panel renders as a bottom Drawer instead (see below), so a
                selected step's detail doesn't scroll off-screen once the grid stacks. */}
            {isDesktop && (
              <Card withBorder shadow="sm" radius="lg" padding="lg">
                <Text fw={600} mb="md">Step Bubble Insight</Text>
                {!selectedStepResult ? (
                  <Alert icon={<IconPlayerPlay size={14} />} color="blue" variant="light">
                    Select any step bubble to inspect live details.
                  </Alert>
                ) : (
                  <StepResultPanel
                    projectId={projectId}
                    stepResult={selectedStepResult}
                    stepType={selectedStepType}
                    stepEvents={selectedStepEvents}
                    liveTokenMetrics={selectedStepLiveTokenMetrics}
                    artifactJson={artifactJson}
                    artifactError={artifactError}
                    artifactLoading={artifactLoading}
                  />
                )}
              </Card>
            )}
          </SimpleGrid>
        </motion.div>

        {!isDesktop && (
          <Drawer
            opened={selectedStepResult !== null}
            onClose={() => setSelectedStepResult(null)}
            position="bottom"
            size="85%"
            title={<Text fw={700} size="lg">Step Bubble Insight</Text>}
          >
            {selectedStepResult && (
              <StepResultPanel
                projectId={projectId}
                stepResult={selectedStepResult}
                stepType={selectedStepType}
                stepEvents={selectedStepEvents}
                liveTokenMetrics={selectedStepLiveTokenMetrics}
                artifactJson={artifactJson}
                artifactError={artifactError}
                artifactLoading={artifactLoading}
              />
            )}
          </Drawer>
        )}

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
