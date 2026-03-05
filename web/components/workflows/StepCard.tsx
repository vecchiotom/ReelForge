'use client';

import { useState } from 'react';
import { Card, Group, Text, ActionIcon, TextInput, Stack, Textarea, Collapse, Button } from '@mantine/core';
import { IconGripVertical, IconTrash, IconSettings } from '@tabler/icons-react';
import { useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { AgentPicker } from './AgentPicker';
import { StepTypeSelector } from './StepTypeSelector';
import { StepTypeBadge } from './StepTypeBadge';
import { ConditionalStepConfig } from './ConditionalStepConfig';
import { ForEachStepConfig } from './ForEachStepConfig';
import { ReviewLoopStepConfig } from './ReviewLoopStepConfig';
import type { StepData } from './WorkflowStepList';

interface StepCardProps {
  step: StepData;
  stepNumber: number;
  onChange: (updates: Partial<StepData>) => void;
  onRemove: () => void;
}

export function StepCard({ step, stepNumber, onChange, onRemove }: StepCardProps) {
  const [advancedOpen, setAdvancedOpen] = useState(false);
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({ id: step.id });

  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
    opacity: isDragging ? 0.5 : 1,
  };

  const showAgentPicker = step.stepType !== 'Conditional';

  return (
    <Card ref={setNodeRef} style={style} withBorder padding="sm" radius="md">
      <Stack gap="sm">
        <Group gap="sm" wrap="nowrap">
          <ActionIcon variant="subtle" {...attributes} {...listeners} style={{ cursor: 'grab' }}>
            <IconGripVertical size={16} />
          </ActionIcon>
          <Text size="sm" fw={700} c="dimmed" w={24} ta="center">{stepNumber}</Text>
          <StepTypeBadge stepType={step.stepType} />
          <TextInput
            placeholder="Step label"
            value={step.label}
            onChange={(e) => onChange({ label: e.target.value })}
            style={{ flex: 1 }}
            size="sm"
          />
          <ActionIcon color="red" variant="subtle" onClick={onRemove}>
            <IconTrash size={16} />
          </ActionIcon>
        </Group>

        <Group gap="sm" grow>
          <StepTypeSelector value={step.stepType} onChange={(stepType) => onChange({ stepType })} />
          {showAgentPicker && (
            <AgentPicker value={step.agentDefinitionId} onChange={(agentDefinitionId) => onChange({ agentDefinitionId })} />
          )}
        </Group>

        {step.stepType === 'Conditional' && (
          <ConditionalStepConfig
            conditionExpression={step.conditionExpression}
            trueBranchStepOrder={step.trueBranchStepOrder}
            falseBranchStepOrder={step.falseBranchStepOrder}
            onChange={onChange}
          />
        )}

        {step.stepType === 'ForEach' && (
          <ForEachStepConfig
            loopSourceExpression={step.loopSourceExpression}
            maxIterations={step.maxIterations}
            onChange={onChange}
          />
        )}

        {step.stepType === 'ReviewLoop' && (
          <ReviewLoopStepConfig
            minScore={step.minScore}
            maxIterations={step.maxIterations}
            loopTargetStepOrder={step.loopTargetStepOrder}
            onChange={onChange}
          />
        )}

        <Button
          variant="subtle"
          size="compact-xs"
          leftSection={<IconSettings size={14} />}
          onClick={() => setAdvancedOpen(!advancedOpen)}
        >
          Advanced
        </Button>
        <Collapse in={advancedOpen}>
          <Textarea
            label="Input Mapping JSON"
            description="JSON object mapping step inputs from previous step outputs"
            placeholder='{"input": "{{steps.1.output}}"}'
            value={step.inputMappingJson || ''}
            onChange={(e) => onChange({ inputMappingJson: e.target.value || null })}
            size="sm"
            rows={3}
          />
        </Collapse>
      </Stack>
    </Card>
  );
}
