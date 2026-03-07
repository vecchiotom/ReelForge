'use client';

import { NumberInput, Stack, Divider, Text } from '@mantine/core';
import { OutputSchemaViewer } from './OutputSchemaViewer';
import type { StepData } from './WorkflowStepList';

interface ReviewLoopStepConfigProps {
  minScore: number | null;
  maxIterations: number;
  loopTargetStepOrder: number | null;
  onChange: (updates: {
    minScore?: number | null;
    maxIterations?: number;
    loopTargetStepOrder?: number | null;
  }) => void;
  previousSteps?: StepData[];
  currentStepIndex?: number;
}

export function ReviewLoopStepConfig({
  minScore,
  maxIterations,
  loopTargetStepOrder,
  onChange,
  previousSteps = [],
  currentStepIndex = 0,
}: ReviewLoopStepConfigProps) {
  return (
    <Stack gap="md">
      <Stack gap="xs">
        <NumberInput
          label="Minimum Score"
          description="Score threshold (1-10) to pass the review"
          min={1}
          max={10}
          value={minScore ?? 9}
          onChange={(v) => onChange({ minScore: typeof v === 'number' ? v : null })}
          size="sm"
        />
        <NumberInput
          label="Max Iterations"
          description="Maximum review loop iterations before giving up"
          min={1}
          max={10}
          value={maxIterations}
          onChange={(v) => onChange({ maxIterations: typeof v === 'number' ? v : 3 })}
          size="sm"
        />
        <NumberInput
          label="Loop Target Step"
          description="Step order to loop back to on low score (leave empty for previous step)"
          min={1}
          value={loopTargetStepOrder ?? undefined}
          onChange={(v) => onChange({ loopTargetStepOrder: typeof v === 'number' ? v : null })}
          size="sm"
          placeholder="Optional"
        />
        <Text size="xs" c="dimmed">
          The ReviewLoop agent expects the target step to produce a 'score' field (1-10) in its output.
        </Text>
      </Stack>

      <Divider />

      <OutputSchemaViewer previousSteps={previousSteps} currentStepIndex={currentStepIndex} />
    </Stack>
  );
}
