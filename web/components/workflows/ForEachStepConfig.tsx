'use client';

import { TextInput, NumberInput, Stack, Divider } from '@mantine/core';
import { OutputSchemaViewer } from './OutputSchemaViewer';
import type { StepData } from './WorkflowStepList';

interface ForEachStepConfigProps {
  loopSourceExpression: string | null;
  maxIterations: number;
  onChange: (updates: {
    loopSourceExpression?: string | null;
    maxIterations?: number;
  }) => void;
  previousSteps?: StepData[];
  currentStepIndex?: number;
}

export function ForEachStepConfig({
  loopSourceExpression,
  maxIterations,
  onChange,
  previousSteps = [],
  currentStepIndex = 0,
}: ForEachStepConfigProps) {
  return (
    <Stack gap="md">
      <Stack gap="xs">
        <TextInput
          label="Loop Source Expression"
          description="Expression that resolves to a collection from previous step output"
          placeholder="e.g. {{steps.1.components}}"
          value={loopSourceExpression || ''}
          onChange={(e) => onChange({ loopSourceExpression: e.target.value || null })}
          size="sm"
        />
        <NumberInput
          label="Max Iterations"
          description="Maximum number of loop iterations"
          min={1}
          max={50}
          value={maxIterations}
          onChange={(v) => onChange({ maxIterations: typeof v === 'number' ? v : 3 })}
          size="sm"
        />
      </Stack>

      <Divider />

      <OutputSchemaViewer previousSteps={previousSteps} currentStepIndex={currentStepIndex} />
    </Stack>
  );
}
