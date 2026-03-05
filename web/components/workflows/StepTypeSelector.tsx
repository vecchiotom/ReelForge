'use client';

import { Select } from '@mantine/core';
import { STEP_TYPE_LABELS, STEP_TYPE_DESCRIPTIONS } from '@/lib/utils/constants';
import type { StepType } from '@/lib/types/workflow';

const stepTypeOptions = (Object.keys(STEP_TYPE_LABELS) as StepType[]).map((key) => ({
  value: key,
  label: STEP_TYPE_LABELS[key],
  description: STEP_TYPE_DESCRIPTIONS[key],
}));

interface StepTypeSelectorProps {
  value: StepType;
  onChange: (value: StepType) => void;
}

export function StepTypeSelector({ value, onChange }: StepTypeSelectorProps) {
  return (
    <Select
      label="Step Type"
      data={stepTypeOptions}
      value={value}
      onChange={(v) => v && onChange(v as StepType)}
      size="sm"
    />
  );
}
