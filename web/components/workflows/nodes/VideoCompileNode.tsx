'use client';

import { memo, useState } from 'react';
import { Handle, Position } from '@xyflow/react';
import { Card, Group, Text, ActionIcon, Badge, Stack, TextInput, Tooltip } from '@mantine/core';
import { IconTrash, IconScissors } from '@tabler/icons-react';
import { motion } from 'framer-motion';
import { VideoCompileStepConfig, createDefaultVideoCompileStepConfig } from '../VideoCompileStepConfig';
import type { StepData } from '../WorkflowStepList';

interface VideoCompileNodeData {
  step: StepData;
  stepNumber: number;
  allSteps: StepData[];
  currentStepIndex: number;
  onChange: (updates: Partial<StepData>) => void;
  onRemove: () => void;
}

export const VideoCompileNode = memo(({ data }: { data: VideoCompileNodeData }) => {
  const { step, stepNumber, allSteps, currentStepIndex, onChange, onRemove } = data;
  const [expanded, setExpanded] = useState(false);

  const config = step.videoCompileConfig ?? createDefaultVideoCompileStepConfig();

  return (
    <>
      <Handle type="target" position={Position.Top} style={{ background: '#6366f1' }} />

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
            border: '2px solid #6366f1',
            background: 'linear-gradient(135deg, #ffffff 0%, #eef2ff 100%)',
            cursor: 'pointer',
          }}
          onMouseEnter={() => setExpanded(true)}
          onMouseLeave={() => setExpanded(false)}
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
                    background: 'linear-gradient(135deg, #6366f1 0%, #4338ca 100%)',
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
                  gradient={{ from: 'indigo', to: 'grape', deg: 90 }}
                  leftSection={<IconScissors size={14} />}
                >
                  COMPILE VIDEO
                </Badge>
                <Badge size="sm" variant="outline" color="indigo">
                  {config.mode}
                </Badge>
                <Badge size="sm" variant="outline" color="gray">
                  crf {config.crf}
                </Badge>
              </Group>
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

            {/* Label */}
            <TextInput
              placeholder="Step label (e.g. Cut final edit)"
              value={step.label}
              onChange={(e) => onChange({ label: e.target.value })}
              size="sm"
              styles={{ input: { fontWeight: 600, fontSize: 15, border: 'none', background: 'transparent', padding: 0 } }}
              onClick={(e) => e.stopPropagation()}
            />

            <Text size="xs" c="dimmed">
              Deterministic ffmpeg-based cutting from an editorial decision. No model call.
            </Text>

            {/* Expanded: full config form */}
            {expanded && (
              <VideoCompileStepConfig
                config={config}
                onChange={(next) => onChange({ videoCompileConfig: next })}
                allSteps={allSteps}
                currentStepIndex={currentStepIndex}
              />
            )}
          </Stack>
        </Card>
      </motion.div>

      <Handle type="source" position={Position.Bottom} style={{ background: '#6366f1' }} />
    </>
  );
});

VideoCompileNode.displayName = 'VideoCompileNode';
