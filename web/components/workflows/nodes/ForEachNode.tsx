'use client';

import { memo, useState } from 'react';
import { Handle, Position } from '@xyflow/react';
import { Card, Group, Text, ActionIcon, Badge, Stack, TextInput, NumberInput, Tooltip, Modal, Button, Collapse } from '@mantine/core';
import { IconTrash, IconRepeat, IconSettings, IconChevronDown, IconChevronUp } from '@tabler/icons-react';
import { motion } from 'framer-motion';
import { ForEachStepConfig } from '../ForEachStepConfig';
import type { StepData } from '../WorkflowStepList';

interface ForEachNodeData {
  step: StepData;
  stepNumber: number;
  allSteps: StepData[];
  currentStepIndex: number;
  expanded: boolean;
  pinned: boolean;
  onExpandChange: (stepId: string | null) => void;
  onTogglePin: (stepId: string) => void;
  onChange: (updates: Partial<StepData>) => void;
  onRemove: () => void;
}

export const ForEachNode = memo(({ data }: { data: ForEachNodeData }) => {
  const { step, stepNumber, allSteps, currentStepIndex, expanded, pinned, onExpandChange, onTogglePin, onChange, onRemove } = data;
  const isOpen = expanded || pinned;
  const [configModalOpen, setConfigModalOpen] = useState(false);

  return (
    <>
      <Handle type="target" position={Position.Top} style={{ background: '#06b6d4' }} />
      
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
            width: 320,
            border: '2px solid #06b6d4',
            background: 'linear-gradient(135deg, light-dark(#ffffff, var(--mantine-color-dark-7)) 0%, light-dark(#ecfeff, var(--mantine-color-dark-6)) 100%)',
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
                    background: 'linear-gradient(135deg, #06b6d4 0%, #0891b2 100%)',
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
                  gradient={{ from: 'cyan', to: 'blue', deg: 90 }}
                  leftSection={<IconRepeat size={14} />}
                >
                  FOR EACH
                </Badge>
              </Group>
              <Group gap="xs">
                <Tooltip label="Configure with Schema Help">
                  <ActionIcon
                    color="violet"
                    variant="subtle"
                    onClick={(e) => {
                      e.stopPropagation();
                      setConfigModalOpen(true);
                    }}
                  >
                    <IconSettings size={16} />
                  </ActionIcon>
                </Tooltip>
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
            <Text fw={600} size="sm" c="dimmed">
              {step.label || 'Loop Over Collection'}
            </Text>

            {/* Expanded Content */}
            <Collapse in={isOpen}>
              <Stack gap="xs">
                <TextInput
                  label="Loop Source Expression"
                  placeholder="e.g., {{steps.1.items}}"
                  value={step.loopSourceExpression || ''}
                  onChange={(e) => onChange({ loopSourceExpression: e.target.value })}
                  size="xs"
                  onClick={(e) => e.stopPropagation()}
                />
                
                <NumberInput
                  label="Max Iterations"
                  value={step.maxIterations}
                  onChange={(value) => onChange({ maxIterations: Number(value) || 3 })}
                  min={1}
                  max={100}
                  size="xs"
                  onClick={(e) => e.stopPropagation()}
                />

                <div style={{ background: '#e0f2fe', padding: 8, borderRadius: 4 }}>
                  <Text size="xs" fw={600} c="cyan.8">↻ Loop to Step {step.loopTargetStepOrder || '?'}</Text>
                </div>
              </Stack>
            </Collapse>
          </Stack>
        </Card>
      </motion.div>

      <Handle type="source" position={Position.Bottom} style={{ background: '#06b6d4' }} />

      {/* Configuration Modal with Schema Viewer */}
      <Modal
        opened={configModalOpen}
        onClose={() => setConfigModalOpen(false)}
        title={<Text fw={700}>Configure ForEach Step</Text>}
        size="xl"
        onClick={(e) => e.stopPropagation()}
      >
        <ForEachStepConfig
          loopSourceExpression={step.loopSourceExpression}
          maxIterations={step.maxIterations}
          onChange={(updates) => onChange(updates)}
          previousSteps={allSteps}
          currentStepIndex={currentStepIndex}
        />
        <Group justify="flex-end" mt="md">
          <Button onClick={() => setConfigModalOpen(false)}>Done</Button>
        </Group>
      </Modal>
    </>
  );
});

ForEachNode.displayName = 'ForEachNode';
