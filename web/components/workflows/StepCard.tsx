'use client';

import { useState } from 'react';
import { Card, Group, Text, ActionIcon, TextInput, Stack, Textarea, Collapse, Button, Tooltip } from '@mantine/core';
import { IconChevronUp, IconChevronDown, IconTrash, IconSettings } from '@tabler/icons-react';
import { AgentPicker } from './AgentPicker';
import { StepTypeSelector } from './StepTypeSelector';
import { StepTypeBadge } from './StepTypeBadge';
import { ConditionalStepConfig } from './ConditionalStepConfig';
import { ForEachStepConfig } from './ForEachStepConfig';
import { ReviewLoopStepConfig } from './ReviewLoopStepConfig';
import { ExtractStepConfig, createDefaultExtractStepConfig } from './ExtractStepConfig';
import { VideoAnalyzeStepConfig, createDefaultVideoAnalyzeStepConfig } from './VideoAnalyzeStepConfig';
import { VideoCompileStepConfig, createDefaultVideoCompileStepConfig } from './VideoCompileStepConfig';
import type { StepData } from './WorkflowStepList';

interface StepCardProps {
  step: StepData;
  stepNumber: number;
  allSteps: StepData[];
  currentStepIndex: number;
  projectId?: string;
  onChange: (updates: Partial<StepData>) => void;
  onRemove: () => void;
  onMoveUp: () => void;
  onMoveDown: () => void;
  canMoveUp: boolean;
  canMoveDown: boolean;
}

export function StepCard({
  step,
  stepNumber,
  allSteps,
  currentStepIndex,
  projectId,
  onChange,
  onRemove,
  onMoveUp,
  onMoveDown,
  canMoveUp,
  canMoveDown,
}: StepCardProps) {
  const [advancedOpen, setAdvancedOpen] = useState(false);

  const showAgentPicker =
    step.stepType !== 'Conditional' &&
    step.stepType !== 'Extract' &&
    step.stepType !== 'VideoAnalyze' &&
    step.stepType !== 'VideoCompile';

  return (
    <Card withBorder padding="sm" radius="md">
      <Stack gap="sm">
        <Group gap="sm" wrap="nowrap">
          <Stack gap={2}>
            <Tooltip label="Move up">
              <ActionIcon
                variant="subtle"
                size="sm"
                disabled={!canMoveUp}
                onClick={onMoveUp}
                aria-label="Move step up"
              >
                <IconChevronUp size={14} />
              </ActionIcon>
            </Tooltip>
            <Tooltip label="Move down">
              <ActionIcon
                variant="subtle"
                size="sm"
                disabled={!canMoveDown}
                onClick={onMoveDown}
                aria-label="Move step down"
              >
                <IconChevronDown size={14} />
              </ActionIcon>
            </Tooltip>
          </Stack>
          <Text size="sm" fw={700} c="dimmed" w={24} ta="center">{stepNumber}</Text>
          <StepTypeBadge stepType={step.stepType} />
          <TextInput
            placeholder="Step label"
            value={step.label}
            onChange={(e) => onChange({ label: e.target.value })}
            style={{ flex: 1 }}
            size="sm"
          />
          <ActionIcon color="red" variant="subtle" onClick={onRemove} aria-label="Delete step">
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
            previousSteps={allSteps}
            currentStepIndex={currentStepIndex}
          />
        )}

        {step.stepType === 'ForEach' && (
          <ForEachStepConfig
            loopSourceExpression={step.loopSourceExpression}
            maxIterations={step.maxIterations}
            onChange={onChange}
            previousSteps={allSteps}
            currentStepIndex={currentStepIndex}
          />
        )}

        {step.stepType === 'ReviewLoop' && (
          <ReviewLoopStepConfig
            minScore={step.minScore}
            maxIterations={step.maxIterations}
            loopTargetStepOrder={step.loopTargetStepOrder}
            onChange={onChange}
            previousSteps={allSteps}
            currentStepIndex={currentStepIndex}
          />
        )}

        {step.stepType === 'Extract' && (
          <ExtractStepConfig
            config={step.extractConfig ?? createDefaultExtractStepConfig()}
            onChange={(extractConfig) => onChange({ extractConfig })}
            allSteps={allSteps}
            currentStepIndex={currentStepIndex}
          />
        )}

        {step.stepType === 'VideoAnalyze' && (
          <VideoAnalyzeStepConfig
            config={step.videoAnalyzeConfig ?? createDefaultVideoAnalyzeStepConfig()}
            onChange={(videoAnalyzeConfig) => onChange({ videoAnalyzeConfig })}
            allSteps={allSteps}
            currentStepIndex={currentStepIndex}
            projectId={projectId}
          />
        )}

        {step.stepType === 'VideoCompile' && (
          <VideoCompileStepConfig
            config={step.videoCompileConfig ?? createDefaultVideoCompileStepConfig()}
            onChange={(videoCompileConfig) => onChange({ videoCompileConfig })}
            allSteps={allSteps}
            currentStepIndex={currentStepIndex}
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
