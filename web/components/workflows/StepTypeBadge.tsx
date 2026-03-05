'use client';

import { Badge } from '@mantine/core';
import { STEP_TYPE_COLORS, STEP_TYPE_LABELS } from '@/lib/utils/constants';
import type { StepType } from '@/lib/types/workflow';

export function StepTypeBadge({ stepType }: { stepType: StepType }) {
  return (
    <Badge color={STEP_TYPE_COLORS[stepType] || 'gray'} variant="light" size="sm">
      {STEP_TYPE_LABELS[stepType] || stepType}
    </Badge>
  );
}
