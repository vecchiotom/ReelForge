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
import { IconPlus, IconLayoutGrid } from '@tabler/icons-react';
import { AgentNode } from './nodes/AgentNode';
import { ConditionalNode } from './nodes/ConditionalNode';
import { ForEachNode } from './nodes/ForEachNode';
import { ReviewLoopNode } from './nodes/ReviewLoopNode';
import { ParallelNode } from './nodes/ParallelNode';
import { ExtractNode } from './nodes/ExtractNode';
import { AddStepModal } from './AddStepModal';
import { createDefaultExtractStepConfig } from './ExtractStepConfig';
import { useAgents } from '@/lib/hooks/use-agents';
import type { StepData } from './WorkflowStepList';
import type { StepType } from '@/lib/types/workflow';

const nodeTypes = {
  agent: AgentNode,
  conditional: ConditionalNode,
  forEach: ForEachNode,
  reviewLoop: ReviewLoopNode,
  parallel: ParallelNode,
  extract: ExtractNode,
} satisfies NodeTypes;

/** Maps a workflow StepType to its React Flow node type key. */
const STEP_TYPE_TO_NODE_TYPE: Record<StepType, keyof typeof nodeTypes> = {
  Agent: 'agent',
  Conditional: 'conditional',
  ForEach: 'forEach',
  ReviewLoop: 'reviewLoop',
  Parallel: 'parallel',
  Extract: 'extract',
};

interface FlowchartBuilderProps {
  steps: StepData[];
  onChange: (steps: StepData[]) => void;
}

export function FlowchartBuilder({ steps, onChange }: FlowchartBuilderProps) {
  const [addModalOpen, setAddModalOpen] = useState(false);
  const [nodes, setNodes, onNodesChange] = useNodesState<Node>([]);
  const [edges, setEdges, onEdgesChange] = useEdgesState<Edge>([]);
  const { data: agents } = useAgents();

  // Convert steps to nodes and edges
  useMemo(() => {
    const newNodes: Node[] = [];
    const newEdges: Edge[] = [];

    steps.forEach((step, index) => {
      const nodeType = STEP_TYPE_TO_NODE_TYPE[step.stepType] ?? 'agent';

      newNodes.push({
        id: step.id,
        type: nodeType,
        position: { x: 250, y: index * 180 + 50 },
        data: {
          step,
          stepNumber: index + 1,
          allSteps: steps,
          currentStepIndex: index,
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
  }, [steps, onChange, setNodes, setEdges]);

  const onConnect = useCallback(
    (params: Connection) => setEdges((existingEdges) => addEdge(params, existingEdges)),
    [setEdges],
  );

  const handleAddStep = (stepType: StepType) => {
    // Extract steps run no model — auto-assign the built-in, non-LLM ExtractTransform agent so
    // the non-nullable AgentDefinitionId FK is always satisfied without user action.
    const extractTransformAgentId = agents?.find((a) => a.agentType === 'ExtractTransform')?.id ?? '';

    const newStep: StepData = {
      id: `step-${Date.now()}`,
      label: '',
      agentDefinitionId: stepType === 'Extract' ? extractTransformAgentId : '',
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
      />
    </>
  );
}

export function FlowchartBuilderWrapper(props: FlowchartBuilderProps) {
  return (
    <ReactFlowProvider>
      <FlowchartBuilder {...props} />
    </ReactFlowProvider>
  );
}
