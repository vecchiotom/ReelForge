'use client';

import { use, useEffect, useState, useCallback } from 'react';
import { Stack, Card, Group, Text, Badge, Loader, Center, Progress, Timeline, Paper, Alert, Modal, Divider, ScrollArea } from '@mantine/core';
import { IconPlayerPlay, IconCheck, IconX, IconClock, IconAlertCircle } from '@tabler/icons-react';
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
import type { WorkflowExecution, WorkflowDefinition, WorkflowStepResult } from '@/lib/types/workflow';
import { formatDate, formatDurationLong } from '@/lib/utils/format';
import { JsonViewer } from '@/components/workflows/JsonViewer';

interface WorkflowEvent {
  type: string;
  executionId: string;
  timestamp: string;
  data: unknown;
}

function ExecutionDetailPageInner({ params }: { params: Promise<{ id: string; workflowId: string; executionId: string }> }) {
  const { id: projectId, workflowId, executionId } = use(params);

  const [execution, setExecution] = useState<WorkflowExecution | null>(null);
  const [workflow, setWorkflow] = useState<WorkflowDefinition | null>(null);
  const [loading, setLoading] = useState(true);
  const [events, setEvents] = useState<WorkflowEvent[]>([]);
  const [nodes, setNodes, onNodesChange] = useNodesState<Node>([]);
  const [edges, setEdges, onEdgesChange] = useEdgesState<Edge>([]);
  const [selectedStepResult, setSelectedStepResult] = useState<WorkflowStepResult | null>(null);
  const [modalOpened, setModalOpened] = useState(false);

  // Initialize flow visualization
  const initializeFlow = useCallback((wf: WorkflowDefinition, exec: WorkflowExecution) => {
    const newNodes: Node[] = [];
    const newEdges: Edge[] = [];

    wf.steps.forEach((step, index) => {
      const stepResult = exec.stepResults.find((r) => r.workflowStepId === step.id);
      const status = stepResult?.status || 'Pending';

      const color =
        status === 'Completed' ? '#10b981'
          : status === 'Running' ? '#3b82f6'
            : status === 'Failed' ? '#ef4444'
              : '#9ca3af';

      newNodes.push({
        id: step.id,
        type: 'default',
        position: { x: 250, y: index * 150 + 50 },
        data: {
          label: (
            <div style={{ padding: 8, minWidth: 200, cursor: 'pointer' }}>
              <Group gap="xs" mb={4}>
                <Badge size="xs" variant="filled" style={{ background: color }}>
                  {status}
                </Badge>
                <Text size="xs" fw={600}>{step.label}</Text>
              </Group>
              {stepResult && (
                <Text size="xs" c="dimmed">{Math.round(stepResult.durationMs)}ms</Text>
              )}
            </div>
          ),
          stepResult, // Store the step result in node data
        },
        style: {
          border: `2px solid ${color}`,
          borderRadius: 8,
          background: status === 'Running' ? `${color}11` : 'white',
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
          apiFetch<WorkflowExecution>(`/api/v1/projects/${projectId}/executions/${executionId}`),
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

  // SSE connection for real-time updates
  useEffect(() => {
    if (!execution || execution.status === 'Passed' || execution.status === 'Failed') {
      return;
    }

    // TODO: Server doesn't provide per-execution SSE yet
    // For now, poll the execution endpoint
    const intervalId = setInterval(async () => {
      try {
        const data = await apiFetch<WorkflowExecution>(`/api/v1/projects/${projectId}/executions/${executionId}`);
        setExecution(data);
        if (workflow) initializeFlow(workflow, data);
        
        // Stop polling if execution completed
        if (data.status === 'Passed' || data.status === 'Failed') {
          clearInterval(intervalId);
        }
      } catch (error) {
        console.error('Failed to refresh execution:', error);
      }
    }, 2000); // Poll every 2 seconds

    return () => {
      clearInterval(intervalId);
    };
  }, [execution, projectId, executionId, workflow, initializeFlow]);

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
          <Card withBorder shadow="sm" radius="lg" padding="lg">
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
                  <Text size="sm" c="dimmed">
                    {duration > 0 ? formatDurationLong(duration) : 'Not started'}
                  </Text>
                </Group>

                {execution.status === 'Running' && (
                  <Loader size="sm" />
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
              </Group>
            </Stack>
          </Card>
        </motion.div>

        {/* Flow Visualization */}
        <motion.div
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.4, delay: 0.2 }}
        >
          <Card withBorder shadow="sm" radius="lg" padding="lg">
            <Text fw={600} mb="md">Workflow Flow</Text>
            <div style={{ height: 500, border: '1px solid var(--mantine-color-gray-3)', borderRadius: 8 }}>
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
        </motion.div>

        {/* Event Timeline */}
        <motion.div
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.4, delay: 0.4 }}
        >
          <Card withBorder shadow="sm" radius="lg" padding="lg">
            <Text fw={600} mb="md">Real-Time Events</Text>
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
                          {JSON.stringify(event.data, null, 2)}
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
            box-shadow: 0 0 0 0 rgba(59, 130, 246, 0.7);
          }
          50% {
            box-shadow: 0 0 0 10px rgba(59, 130, 246, 0);
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
