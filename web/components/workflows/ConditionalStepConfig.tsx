'use client';

import { TextInput, Stack, Text, Divider } from '@mantine/core';
import { OutputSchemaViewer } from './OutputSchemaViewer';
import type { StepData } from './WorkflowStepList';

interface ConditionalStepConfigProps {
  conditionExpression: string | null;
  trueBranchStepOrder: string | null;
  falseBranchStepOrder: string | null;
  onChange: (updates: {
    conditionExpression?: string | null;
    trueBranchStepOrder?: string | null;
    falseBranchStepOrder?: string | null;
  }) => void;
  previousSteps?: StepData[];
  currentStepIndex?: number;
}

export function ConditionalStepConfig({
  conditionExpression,
  trueBranchStepOrder,
  falseBranchStepOrder,
  onChange,
  previousSteps = [],
  currentStepIndex = 0,
}: ConditionalStepConfigProps) {
  return (
    <Stack gap="md">
      <Stack gap="xs">
        <TextInput
          label="Condition Expression"
          description="NCalc expression evaluated at runtime"
          placeholder="[score] >= 9"
          value={conditionExpression || ''}
          onChange={(e) => onChange({ conditionExpression: e.target.value || null })}
          size="sm"
        />
        <Text size="xs" c="dimmed">Uses NCalc syntax. Variables from previous step outputs are available in [brackets].</Text>
        <TextInput
          label="True Branch Step"
          description="Step order to jump to when condition is true"
          placeholder="e.g. 3"
          value={trueBranchStepOrder || ''}
          onChange={(e) => onChange({ trueBranchStepOrder: e.target.value || null })}
          size="sm"
        />
        <TextInput
          label="False Branch Step"
          description="Step order to jump to when condition is false"
          placeholder="e.g. 5"
          value={falseBranchStepOrder || ''}
          onChange={(e) => onChange({ falseBranchStepOrder: e.target.value || null })}
          size="sm"
        />
      </Stack>

      <Divider />

      <OutputSchemaViewer previousSteps={previousSteps} currentStepIndex={currentStepIndex} />
    </Stack>
  );
}
