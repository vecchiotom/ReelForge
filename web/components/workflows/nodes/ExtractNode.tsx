'use client';

import { memo, useState } from 'react';
import { Handle, Position } from '@xyflow/react';
import { Card, Group, Text, ActionIcon, Badge, Stack, TextInput, Tooltip } from '@mantine/core';
import { IconTrash, IconFilterCog } from '@tabler/icons-react';
import { motion } from 'framer-motion';
import { ExtractStepConfig, createDefaultExtractStepConfig } from '../ExtractStepConfig';
import type { StepData } from '../WorkflowStepList';

interface ExtractNodeData {
  step: StepData;
  stepNumber: number;
  allSteps: StepData[];
  currentStepIndex: number;
  onChange: (updates: Partial<StepData>) => void;
  onRemove: () => void;
}

export const ExtractNode = memo(({ data }: { data: ExtractNodeData }) => {
  const { step, stepNumber, allSteps, currentStepIndex, onChange, onRemove } = data;
  const [expanded, setExpanded] = useState(false);

  const config = step.extractConfig ?? createDefaultExtractStepConfig();

  return (
    <>
      <Handle type="target" position={Position.Top} style={{ background: '#a855f7' }} />

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
            border: '2px solid #a855f7',
            background: 'linear-gradient(135deg, #ffffff 0%, #faf5ff 100%)',
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
                    background: 'linear-gradient(135deg, #a855f7 0%, #7e22ce 100%)',
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
                  gradient={{ from: 'grape', to: 'violet', deg: 90 }}
                  leftSection={<IconFilterCog size={14} />}
                >
                  EXTRACT
                </Badge>
                <Badge size="sm" variant="outline" color="grape">
                  {config.operation.toLowerCase()}
                </Badge>
                <Badge size="sm" variant="outline" color="gray">
                  ≤{config.maxOutputChars.toLocaleString()} chars
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
              placeholder="Step label (e.g. Reduce component inventory)"
              value={step.label}
              onChange={(e) => onChange({ label: e.target.value })}
              size="sm"
              styles={{ input: { fontWeight: 600, fontSize: 15, border: 'none', background: 'transparent', padding: 0 } }}
              onClick={(e) => e.stopPropagation()}
            />

            <Text size="xs" c="dimmed">
              Deterministic, code-only projection — no model call, no tokens used.
            </Text>

            {/* Expanded: full config form */}
            {expanded && (
              <ExtractStepConfig
                config={config}
                onChange={(next) => onChange({ extractConfig: next })}
                allSteps={allSteps}
                currentStepIndex={currentStepIndex}
              />
            )}
          </Stack>
        </Card>
      </motion.div>

      <Handle type="source" position={Position.Bottom} style={{ background: '#a855f7' }} />
    </>
  );
});

ExtractNode.displayName = 'ExtractNode';
