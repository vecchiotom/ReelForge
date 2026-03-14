'use client';

import { use, useEffect, useState, useCallback } from 'react';
import { Stack, Card, Group, Text, Badge, Loader, Center, Progress, Timeline, Paper, Alert, Modal, Divider, ScrollArea, Button, SimpleGrid } from '@mantine/core';
import { IconPlayerPlay, IconCheck, IconX, IconClock, IconAlertCircle, IconPlayerStop, IconBolt, IconActivity } from '@tabler/icons-react';
import { motion, AnimatePresence } from 'framer-motion';
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
import { getExecution, stopExecution } from '@/lib/api/executions';
import type { WorkflowExecution, WorkflowDefinition, WorkflowStepResult } from '@/lib/types/workflow';
import { formatDate, formatDurationLong } from '@/lib/utils/format';
import { JsonViewer } from '@/components/workflows/JsonViewer';
import { useExecutionStream } from '@/lib/hooks/use-execution-stream';

function isTerminalExecutionStatus(status: WorkflowExecution['status'] | undefined): boolean {
  return status === 'Passed' || status === 'Failed' || status === 'Cancelled';
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
                  <Text size="xs" c="dimmed">Live Tokens</Text>
                  <Text size="lg" fw={700}>
                    <Group gap={6} wrap="nowrap">
                      <IconBolt size={14} />
                      <span>{metrics.totalTokens.toLocaleString()}</span>
                    </Group>
                  </Text>
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
                      <Text size="xs" c="dimmed">Tokens</Text>
                      <Text fw={700}>{selectedStepResult.tokensUsed.toLocaleString()}</Text>
                    </Paper>
                  </Group>
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
            <AnimatePresence>
              {events.length === 0 ? (
                <Text size="sm" c="dimmed">Waiting for events...</Text>
              ) : (
                <Timeline active={events.length - 1} bulletSize={24} lineWidth={2}>
                  {events.map((event, i) => (
                    <Timeline.Item
                      key={i}
                      bullet={
                        event.type === 'execution.completed' ? <IconCheck size={12} />
                          : event.type === 'execution.failed' ? <IconX size={12} />
                            : <IconPlayerPlay size={12} />
                      }
                      title={
                        <Group gap="xs">
                          <Badge size="sm">{event.type}</Badge>
                          <Text size="xs" c="dimmed">{new Date(event.timestamp).toLocaleTimeString()}</Text>
                        </Group>
                      }
                    >
                      <motion.div
                        initial={{ opacity: 0, x: -20 }}
                        animate={{ opacity: 1, x: 0 }}
                        transition={{ duration: 0.3 }}
                      >
                        <Text size="xs" c="dimmed" mt={4} style={{ whiteSpace: 'pre-wrap' }}>
                          {JSON.stringify(event.payload, null, 2)}
                        </Text>
                      </motion.div>
                    </Timeline.Item>
                  ))}
                </Timeline>
              )}
            </AnimatePresence>
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
