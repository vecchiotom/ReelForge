'use client';

import { Select } from '@mantine/core';
import { STEP_TYPE_LABELS, STEP_TYPE_DESCRIPTIONS } from '@/lib/utils/constants';
import type { StepType } from '@/lib/types/workflow';

// GraphicsRoom and ColorGradeRoom are display-only for now: the builder has no config editor for it yet (it's
// provisioned by the video-derush-edit-graphics-room template), and a step created here with no
// graphicsRoomConfigJson would hard-fail at execution — so it's excluded from the selectable
// options while STEP_TYPE_LABELS/COLORS still know it for badges on existing steps. EditRoom has
// a real config editor (EditRoomStepConfigEditor) and is fully selectable.
const stepTypeOptions = (Object.keys(STEP_TYPE_LABELS) as StepType[])
  .filter((key) => key !== 'GraphicsRoom' && key !== 'ColorGradeRoom')
  .map((key) => ({
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
