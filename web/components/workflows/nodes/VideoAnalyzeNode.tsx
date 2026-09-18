'use client';

import { memo } from 'react';
import { Handle, Position } from '@xyflow/react';
import { Card, Group, Text, ActionIcon, Badge, Stack, TextInput, Tooltip, Collapse } from '@mantine/core';
import { IconTrash, IconWaveSine, IconChevronDown, IconChevronUp } from '@tabler/icons-react';
import { motion } from 'framer-motion';
import { VideoAnalyzeStepConfig, createDefaultVideoAnalyzeStepConfig } from '../VideoAnalyzeStepConfig';
import type { StepData } from '../WorkflowStepList';

interface VideoAnalyzeNodeData {
  step: StepData;
  stepNumber: number;
  allSteps: StepData[];
  currentStepIndex: number;
  projectId?: string;
  expanded: boolean;
  pinned: boolean;
  onExpandChange: (stepId: string | null) => void;
  onTogglePin: (stepId: string) => void;
  onChange: (updates: Partial<StepData>) => void;
  onRemove: () => void;
}

export const VideoAnalyzeNode = memo(({ data }: { data: VideoAnalyzeNodeData }) => {
  const { step, stepNumber, allSteps, currentStepIndex, projectId, expanded, pinned, onExpandChange, onTogglePin, onChange, onRemove } = data;
  const isOpen = expanded || pinned;

  const config = step.videoAnalyzeConfig ?? createDefaultVideoAnalyzeStepConfig();

  return (
    <>
      <Handle type="target" position={Position.Top} style={{ background: '#3b82f6' }} />

      <motion.div
        initial={{ scale: 0.8, opacity: 0 }}
        animate={{ scale: 1, opacity: 1 }}
        transition={{ duration: 0.3 }}
      >
        <Card
          shadow="md"
          padding="md"
          radius="lg"
          style={{
            width: 360,
            border: '2px solid #3b82f6',
            background: 'linear-gradient(135deg, light-dark(#ffffff, var(--mantine-color-dark-7)) 0%, light-dark(#eff6ff, var(--mantine-color-dark-6)) 100%)',
            cursor: 'pointer',
          }}
          onMouseEnter={() => onExpandChange(step.id)}
          onMouseLeave={() => onExpandChange(null)}
        >
          <Stack gap="sm">
            {/* Header */}
            <Group justify="space-between" wrap="nowrap">
              <Group gap="xs">
                <div
                  style={{
                    width: 32,
                    height: 32,
                    borderRadius: '50%',
                    background: 'linear-gradient(135deg, #3b82f6 0%, #1d4ed8 100%)',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    color: 'white',
                    fontWeight: 700,
                    fontSize: 14,
                  }}
                >
                  {stepNumber}
                </div>
                <Badge
                  size="lg"
                  variant="gradient"
                  gradient={{ from: 'blue', to: 'indigo', deg: 90 }}
                  leftSection={<IconWaveSine size={14} />}
                >
                  ANALYZE VIDEO
                </Badge>
                <Badge size="sm" variant="outline" color="blue">
                  {config.source
                    ? config.source.kind
                    : `${config.sources?.length ?? 0} sources`}
                </Badge>
                <Badge size="sm" variant="outline" color="gray">
                  ASR: {config.transcription}
                </Badge>
              </Group>
              <Group gap="xs">
                <Tooltip label={pinned ? 'Collapse' : 'Expand'}>
                  <ActionIcon
                    color="gray"
                    variant="subtle"
                    onClick={(e) => {
                      e.stopPropagation();
                      onTogglePin(step.id);
                    }}
                  >
                    {pinned ? <IconChevronUp size={16} /> : <IconChevronDown size={16} />}
                  </ActionIcon>
                </Tooltip>
                <Tooltip label="Delete Step">
                  <ActionIcon
                    color="red"
                    variant="subtle"
                    onClick={(e) => {
                      e.stopPropagation();
                      onRemove();
                    }}
                  >
                    <IconTrash size={16} />
                  </ActionIcon>
                </Tooltip>
              </Group>
            </Group>

            {/* Label */}
            <TextInput
              placeholder="Step label (e.g. Derush raw footage)"
              value={step.label}
              onChange={(e) => onChange({ label: e.target.value })}
              size="sm"
              styles={{ input: { fontWeight: 600, fontSize: 15, border: 'none', background: 'transparent', padding: 0 } }}
              onClick={(e) => e.stopPropagation()}
            />

            <Text size="xs" c="dimmed">
              Deterministic ffmpeg-based derushing: silence, shot, and transcript analysis. No model call.
            </Text>

            {/* Expanded: full config form */}
            <Collapse in={isOpen}>
              <VideoAnalyzeStepConfig
                config={config}
                onChange={(next) => onChange({ videoAnalyzeConfig: next })}
                allSteps={allSteps}
                currentStepIndex={currentStepIndex}
                projectId={projectId}
              />
            </Collapse>
          </Stack>
        </Card>
      </motion.div>

      <Handle type="source" position={Position.Bottom} style={{ background: '#3b82f6' }} />
    </>
  );
});

VideoAnalyzeNode.displayName = 'VideoAnalyzeNode';
