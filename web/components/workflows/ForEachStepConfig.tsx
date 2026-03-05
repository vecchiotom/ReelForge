'use client';

import { TextInput, NumberInput, Stack } from '@mantine/core';

interface ForEachStepConfigProps {
  loopSourceExpression: string | null;
  maxIterations: number;
  onChange: (updates: {
    loopSourceExpression?: string | null;
    maxIterations?: number;
  }) => void;
}

export function ForEachStepConfig({
  loopSourceExpression,
  maxIterations,
  onChange,
}: ForEachStepConfigProps) {
  return (
    <Stack gap="xs">
      <TextInput
        label="Loop Source Expression"
        description="Expression that resolves to a collection from previous step output"
        placeholder="e.g. components"
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
  );
}
