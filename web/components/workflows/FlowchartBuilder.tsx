 'use client';

import { useCallback, useMemo, useState } from 'react';
import {
  ReactFlow,
  Controls,
  Background,
  BackgroundVariant,
  Panel,
  useNodesState,
  useEdgesState,
  addEdge,
  Connection,
  Edge,
  Node,
  NodeTypes,
  MarkerType,
  ReactFlowProvider,
} from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import { Stack, ActionIcon, Tooltip } from '@mantine/core';
import { useMediaQuery } from '@mantine/hooks';
import { IconPlus, IconLayoutGrid } from '@tabler/icons-react';
import { AgentNode } from './nodes/AgentNode';
import { ConditionalNode } from './nodes/ConditionalNode';
import { ForEachNode } from './nodes/ForEachNode';
import { ReviewLoopNode } from './nodes/ReviewLoopNode';
import { ParallelNode } from './nodes/ParallelNode';
import { ExtractNode } from './nodes/ExtractNode';
import { VideoAnalyzeNode } from './nodes/VideoAnalyzeNode';
import { VideoCompileNode } from './nodes/VideoCompileNode';
import { AddStepModal } from './AddStepModal';
import { createDefaultExtractStepConfig } from './ExtractStepConfig';
import { createDefaultVideoAnalyzeStepConfig } from './VideoAnalyzeStepConfig';
import { createDefaultVideoCompileStepConfig } from './VideoCompileStepConfig';
import { useAgents } from '@/lib/hooks/use-agents';
import { WorkflowStepList } from './WorkflowStepList';
import type { StepData } from './WorkflowStepList';
import type { StepType } from '@/lib/types/workflow';

const nodeTypes = {
  agent: AgentNode,
  conditional: ConditionalNode,
  forEach: ForEachNode,
  reviewLoop: ReviewLoopNode,
  parallel: ParallelNode,
  extract: ExtractNode,
  videoAnalyze: VideoAnalyzeNode,
  videoCompile: VideoCompileNode,
} satisfies NodeTypes;

/** Maps a workflow StepType to its React Flow node type key. */
const STEP_TYPE_TO_NODE_TYPE: Record<StepType, keyof typeof nodeTypes> = {
  Agent: 'agent',
  Conditional: 'conditional',
  ForEach: 'forEach',
  ReviewLoop: 'reviewLoop',
  Parallel: 'parallel',
  Extract: 'extract',
  VideoAnalyze: 'videoAnalyze',
  VideoCompile: 'videoCompile',
};

interface FlowchartBuilderProps {
  steps: StepData[];
  onChange: (steps: StepData[]) => void;
  /** Threaded into VideoAnalyzeNode's data so its VideoSourceRef picker can list this project's video files. */
  projectId?: string;
}

export function FlowchartBuilder({ steps, onChange, projectId }: FlowchartBuilderProps) {
  const [addModalOpen, setAddModalOpen] = useState(false);
  const [nodes, setNodes, onNodesChange] = useNodesState<Node>([]);
  const [edges, setEdges, onEdgesChange] = useEdgesState<Edge>([]);
  const { data: agents, isLoading: agentsLoading } = useAgents();

  // Hover-preview expansion (transient) and click-to-pin expansion (sticky) for node bodies.
  // Lifted up here — rather than kept as local state inside each node — so a node's zIndex can be
  // promoted above its siblings' from the single place that actually lays nodes out. Without this,
  // an expanded card's body grows downward into the next node's band and loses the paint-order
  // fight, since every node otherwise shares the same default stacking context.
  const [expandedStepId, setExpandedStepId] = useState<string | null>(null);
  const [pinnedStepIds, setPinnedStepIds] = useState<Set<string>>(new Set());

  const handleExpandChange = useCallback((stepId: string | null) => {
    setExpandedStepId(stepId);
  }, []);

  const handleTogglePin = useCallback((stepId: string) => {
    setPinnedStepIds((prev) => {
      const next = new Set(prev);
      if (next.has(stepId)) {
        next.delete(stepId);
      } else {
        next.add(stepId);
      }
      return next;
    });
  }, []);

  // Convert steps to nodes and edges
  useMemo(() => {
    const newNodes: Node[] = [];
    const newEdges: Edge[] = [];

    steps.forEach((step, index) => {
      const nodeType = STEP_TYPE_TO_NODE_TYPE[step.stepType] ?? 'agent';
      const isExpanded = expandedStepId === step.id;
      const isPinned = pinnedStepIds.has(step.id);

      newNodes.push({
        id: step.id,
        type: nodeType,
        position: { x: 250, y: index * 180 + 50 },
        // React Flow v12 honors a node's own zIndex on its wrapper element — the only thing that
        // can actually outrank a later-in-DOM sibling node's paint order once a card expands.
        zIndex: isExpanded || isPinned ? 1000 : 0,
        data: {
          step,
          stepNumber: index + 1,
          allSteps: steps,
          currentStepIndex: index,
          projectId,
          expanded: isExpanded,
          pinned: isPinned,
          onExpandChange: handleExpandChange,
          onTogglePin: handleTogglePin,
          onChange: (updates: Partial<StepData>) => {
            const newSteps = [...steps];
            newSteps[index] = { ...newSteps[index], ...updates };
            onChange(newSteps);
          },
          onRemove: () => {
            onChange(steps.filter((_, i) => i !== index));
          },
        },
      });

      // Create edges based on step flow
      if (index > 0) {
        newEdges.push({
          id: `e${steps[index - 1].id}-${step.id}`,
          source: steps[index - 1].id,
          target: step.id,
          type: 'smoothstep',
          animated: true,
          markerEnd: {
            type: MarkerType.ArrowClosed,
            color: '#8b5cf6',
          },
          style: {
            stroke: '#8b5cf6',
            strokeWidth: 2,
          },
        });
      }
    });

    setNodes(newNodes);
    setEdges(newEdges);
  }, [steps, onChange, setNodes, setEdges, projectId, expandedStepId, pinnedStepIds, handleExpandChange, handleTogglePin]);

  const onConnect = useCallback(
    (params: Connection) => setEdges((existingEdges) => addEdge(params, existingEdges)),
    [setEdges],
  );

  const handleAddStep = (stepType: StepType) => {
    // Extract, VideoAnalyze and VideoCompile steps run no model — auto-assign the appropriate
    // built-in, non-LLM agent so the non-nullable AgentDefinitionId FK is always satisfied
    // without user action. VideoTransform is a single deterministic-placeholder row that serves
    // both new video step types, exactly as ExtractTransform serves Extract.
    const extractTransformAgentId = agents?.find((a) => a.agentType === 'ExtractTransform')?.id ?? '';
    const videoTransformAgentId = agents?.find((a) => a.agentType === 'VideoTransform')?.id ?? '';

    let agentDefinitionId = '';
    if (stepType === 'Extract') agentDefinitionId = extractTransformAgentId;
    if (stepType === 'VideoAnalyze' || stepType === 'VideoCompile') agentDefinitionId = videoTransformAgentId;

    const newStep: StepData = {
      id: `step-${Date.now()}`,
      label: '',
      agentDefinitionId,
      stepType,
      conditionExpression: null,
      loopSourceExpression: null,
      loopTargetStepOrder: null,
      maxIterations: 3,
      minScore: null,
      inputMappingJson: null,
      agentInputContextMode: null,
      selectedPriorStepOrders: [],
      trueBranchStepOrder: null,
      falseBranchStepOrder: null,
      parallelAgentIds: [],
      extractConfig: stepType === 'Extract' ? createDefaultExtractStepConfig() : null,
      videoAnalyzeConfig: stepType === 'VideoAnalyze' ? createDefaultVideoAnalyzeStepConfig() : null,
      videoCompileConfig: stepType === 'VideoCompile' ? createDefaultVideoCompileStepConfig() : null,
    };
    onChange([...steps, newStep]);
    setAddModalOpen(false);
  };

  const handleAutoLayout = () => {
    // Simple vertical layout
    setNodes((nds) =>
      nds.map((node, index) => ({
        ...node,
        position: { x: 250, y: index * 180 + 50 },
      }))
    );
  };

  return (
    <>
      <div style={{ width: '100%', height: '600px', border: '1px solid var(--mantine-color-gray-3)', borderRadius: '8px', overflow: 'hidden' }}>
        <ReactFlow
          nodes={nodes}
          edges={edges}
          onNodesChange={onNodesChange}
          onEdgesChange={onEdgesChange}
          onConnect={onConnect}
          nodeTypes={nodeTypes}
          fitView
          attributionPosition="bottom-right"
        >
          <Background variant={BackgroundVariant.Dots} gap={16} size={1} color="#e9ecef" />
          <Controls showInteractive={false} />
          
          <Panel position="top-right">
            <Stack gap="xs">
              <Tooltip label="Add Step">
                <ActionIcon
                  size="lg"
                  variant="filled"
                  color="violet"
                  onClick={() => setAddModalOpen(true)}
                >
                  <IconPlus size={20} />
                </ActionIcon>
              </Tooltip>
              <Tooltip label="Auto Layout">
                <ActionIcon
                  size="lg"
                  variant="light"
                  color="gray"
                  onClick={handleAutoLayout}
                >
                  <IconLayoutGrid size={20} />
                </ActionIcon>
              </Tooltip>
            </Stack>
          </Panel>
        </ReactFlow>
      </div>

      <AddStepModal
        opened={addModalOpen}
        onClose={() => setAddModalOpen(false)}
        onAdd={handleAddStep}
        nonLlmStepsDisabled={agentsLoading}
      />
    </>
  );
}

export function FlowchartBuilderWrapper({ steps, onChange, projectId }: FlowchartBuilderProps) {
  // Below Mantine's `sm` breakpoint (768px / 48em), drag-and-drop-a-canvas is a poor fit for a
  // touch viewport — render a linear editable list instead. `useMediaQuery` (rather than
  // visibleFrom/hiddenFrom CSS-only hiding) ensures only one of the two ever actually mounts:
  // React Flow is a heavy canvas and we don't want it mounted-but-hidden on a phone burning render
  // budget. Both branches share the same `steps`/`onChange` from the caller, so resizing/rotating
  // across the breakpoint never loses in-progress edits.
  const isDesktop = useMediaQuery('(min-width: 48em)');

  if (!isDesktop) {
    return <WorkflowStepList steps={steps} onChange={onChange} projectId={projectId} />;
  }

  return (
    <ReactFlowProvider>
      <FlowchartBuilder steps={steps} onChange={onChange} projectId={projectId} />
    </ReactFlowProvider>
  );
}
