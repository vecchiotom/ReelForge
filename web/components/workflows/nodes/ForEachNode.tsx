'use client';

import { memo, useState } from 'react';
import { Handle, Position } from '@xyflow/react';
import { Card, Group, Text, ActionIcon, Badge, Stack, TextInput, NumberInput, Tooltip, Modal, Button } from '@mantine/core';
import { IconTrash, IconRepeat, IconSettings } from '@tabler/icons-react';
import { motion } from 'framer-motion';
import { ForEachStepConfig } from '../ForEachStepConfig';
import type { StepData } from '../WorkflowStepList';

interface ForEachNodeData {
  step: StepData;
  stepNumber: number;
  allSteps: StepData[];
  currentStepIndex: number;
  onChange: (updates: Partial<StepData>) => void;
  onRemove: () => void;
}

export const ForEachNode = memo(({ data }: { data: ForEachNodeData }) => {
  const { step, stepNumber, allSteps, currentStepIndex, onChange, onRemove } = data;
  const [expanded, setExpanded] = useState(false);
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
            background: 'linear-gradient(135deg, #ffffff 0%, #ecfeff 100%)',
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
            <motion.div
              initial={false}
              animate={{ height: expanded ? 'auto' : 0, opacity: expanded ? 1 : 0 }}
              transition={{ duration: 0.2 }}
              style={{ overflow: 'hidden' }}
            >
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
            </motion.div>
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
