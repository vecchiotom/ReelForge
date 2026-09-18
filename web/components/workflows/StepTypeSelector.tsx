'use client';

import { Select } from '@mantine/core';
import { STEP_TYPE_LABELS, STEP_TYPE_DESCRIPTIONS } from '@/lib/utils/constants';
import type { StepType } from '@/lib/types/workflow';

// EditRoom/GraphicsRoom are display-only for now: the builder has no config editor for them
// (they're provisioned by the video-derush-edit-room / video-derush-edit-graphics-room
// templates), and a room step created here with no editRoomConfigJson/graphicsRoomConfigJson
// would hard-fail at execution — so they're excluded from the selectable options while
// STEP_TYPE_LABELS/COLORS still know them for badges on existing steps.
const stepTypeOptions = (Object.keys(STEP_TYPE_LABELS) as StepType[])
  .filter((key) => key !== 'EditRoom' && key !== 'GraphicsRoom')
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
