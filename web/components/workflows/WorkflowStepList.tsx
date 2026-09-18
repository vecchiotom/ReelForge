'use client';

import { Stack, Button } from '@mantine/core';
import { IconPlus } from '@tabler/icons-react';
import { StepCard } from './StepCard';
import type {
  AgentInputContextMode,
  ExtractStepConfig as ExtractStepConfigValue,
  StepType,
  VideoAnalyzeStepConfig as VideoAnalyzeStepConfigValue,
  VideoCompileStepConfig as VideoCompileStepConfigValue,
} from '@/lib/types/workflow';

export interface StepData {
  id: string;
  label: string;
  agentDefinitionId: string;
  stepType: StepType;
  conditionExpression: string | null;
  loopSourceExpression: string | null;
  loopTargetStepOrder: number | null;
  maxIterations: number;
  minScore: number | null;
  inputMappingJson: string | null;
  agentInputContextMode: AgentInputContextMode | null;
  selectedPriorStepOrders: number[];
  trueBranchStepOrder: string | null;
  falseBranchStepOrder: string | null;
  /** Ordered list of AgentDefinition IDs to run in parallel (Parallel step type only). */
  parallelAgentIds: string[];
  /** Extract step configuration (Extract step type only). */
  extractConfig: ExtractStepConfigValue | null;
  /** VideoAnalyze step configuration (VideoAnalyze step type only). */
  videoAnalyzeConfig: VideoAnalyzeStepConfigValue | null;
  /** VideoCompile step configuration (VideoCompile step type only). */
  videoCompileConfig: VideoCompileStepConfigValue | null;
  /**
   * Raw JSON-serialized EditRoomStepConfig (EditRoom step type only). Deliberately carried as a
   * raw string (not a parsed object like the extract/video configs): EditRoomStepConfigEditor
   * parses it, edits the handful of fields it exposes, and re-serializes while spread-preserving
   * every field it does not know about — so a template-provisioned config (seats, temperatures,
   * termination mode…) round-trips losslessly through the builder. Optional so every existing
   * StepData construction site keeps compiling unchanged.
   */
  editRoomConfigJson?: string | null;
}

interface WorkflowStepListProps {
  steps: StepData[];
  onChange: (steps: StepData[]) => void;
  /** Threaded into StepCard's VideoAnalyzeStepConfig so its VideoSourceRef picker can list this project's video files. */
  projectId?: string;
}

let nextId = 1;

/** Returns a new array with the item at `from` moved to `to`. Mirrors dnd-kit's `arrayMove` utility
 * without depending on it — this mobile fallback list is reorderable by button tap, not drag. */
function arrayMove<T>(array: T[], from: number, to: number): T[] {
  const next = [...array];
  const [moved] = next.splice(from, 1);
  next.splice(to, 0, moved);
  return next;
}

export function WorkflowStepList({ steps, onChange, projectId }: WorkflowStepListProps) {
  const addStep = () => {
    onChange([
      ...steps,
      {
        id: `new-${nextId++}`,
        label: '',
        agentDefinitionId: '',
        stepType: 'Agent',
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
        extractConfig: null,
        videoAnalyzeConfig: null,
        videoCompileConfig: null,
      },
    ]);
  };

  const updateStep = (index: number, updates: Partial<StepData>) => {
    const newSteps = [...steps];
    newSteps[index] = { ...newSteps[index], ...updates };
    onChange(newSteps);
  };

  const removeStep = (index: number) => {
    onChange(steps.filter((_, i) => i !== index));
  };

  const moveStep = (index: number, direction: -1 | 1) => {
    const target = index + direction;
    if (target < 0 || target >= steps.length) return;
    onChange(arrayMove(steps, index, target));
  };

  return (
    <Stack gap="sm">
      {steps.map((step, index) => (
        <StepCard
          key={step.id}
          step={step}
          stepNumber={index + 1}
          allSteps={steps}
          currentStepIndex={index}
          projectId={projectId}
          onChange={(updates) => updateStep(index, updates)}
          onRemove={() => removeStep(index)}
          onMoveUp={() => moveStep(index, -1)}
          onMoveDown={() => moveStep(index, 1)}
          canMoveUp={index > 0}
          canMoveDown={index < steps.length - 1}
        />
      ))}
      <Button variant="outline" leftSection={<IconPlus size={16} />} onClick={addStep}>
        Add Step
      </Button>
    </Stack>
  );
}
