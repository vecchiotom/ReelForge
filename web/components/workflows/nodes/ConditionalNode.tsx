'use client';

import { memo, useState } from 'react';
import { Handle, Position } from '@xyflow/react';
import { Card, Group, Text, ActionIcon, Badge, Stack, Textarea, Tooltip, Modal, Button } from '@mantine/core';
import { IconTrash, IconGitBranch, IconSettings } from '@tabler/icons-react';
import { motion } from 'framer-motion';
import { ConditionalStepConfig } from '../ConditionalStepConfig';
import type { StepData } from '../WorkflowStepList';

interface ConditionalNodeData {
  step: StepData;
  stepNumber: number;
  allSteps: StepData[];
  currentStepIndex: number;
  onChange: (updates: Partial<StepData>) => void;
  onRemove: () => void;
}

export const ConditionalNode = memo(({ data }: { data: ConditionalNodeData }) => {
  const { step, stepNumber, allSteps, currentStepIndex, onChange, onRemove } = data;
  const [expanded, setExpanded] = useState(false);
  const [configModalOpen, setConfigModalOpen] = useState(false);

  return (
    <>
      <Handle type="target" position={Position.Top} style={{ background: '#f59e0b' }} />
      
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
            border: '2px solid #f59e0b',
            background: 'linear-gradient(135deg, #ffffff 0%, #fffbeb 100%)',
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
                    background: 'linear-gradient(135deg, #f59e0b 0%, #d97706 100%)',
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
                  gradient={{ from: 'yellow', to: 'orange', deg: 90 }}
                  leftSection={<IconGitBranch size={14} />}
                >
                  CONDITIONAL
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
              {step.label || 'Branch Decision'}
            </Text>

            {/* Expanded Content */}
            <motion.div
              initial={false}
              animate={{ height: expanded ? 'auto' : 0, opacity: expanded ? 1 : 0 }}
              transition={{ duration: 0.2 }}
              style={{ overflow: 'hidden' }}
            >
              <Stack gap="xs">
                <Textarea
                  label="Condition Expression"
                  placeholder="e.g., [score] > 8"
                  value={step.conditionExpression || ''}
                  onChange={(e) => onChange({ conditionExpression: e.target.value })}
                  size="xs"
                  rows={2}
                  onClick={(e) => e.stopPropagation()}
                />
                
                <Group grow>
                  <div style={{ background: '#dcfce7', padding: 8, borderRadius: 4 }}>
                    <Text size="xs" fw={600} c="green.8">✓ TRUE → Step {step.trueBranchStepOrder || '?'}</Text>
                  </div>
                  <div style={{ background: '#fee2e2', padding: 8, borderRadius: 4 }}>
                    <Text size="xs" fw={600} c="red.8">✗ FALSE → Step {step.falseBranchStepOrder || '?'}</Text>
                  </div>
                </Group>
              </Stack>
            </motion.div>
          </Stack>
        </Card>
      </motion.div>

      <Handle type="source" position={Position.Bottom} id="true" style={{ left: '25%', background: '#10b981' }} />
      <Handle type="source" position={Position.Bottom} id="false" style={{ left: '75%', background: '#ef4444' }} />

      {/* Configuration Modal with Schema Viewer */}
      <Modal
        opened={configModalOpen}
        onClose={() => setConfigModalOpen(false)}
        title={<Text fw={700}>Configure Conditional Step</Text>}
        size="xl"
        onClick={(e) => e.stopPropagation()}
      >
        <ConditionalStepConfig
          conditionExpression={step.conditionExpression}
          trueBranchStepOrder={step.trueBranchStepOrder?.toString() || null}
          falseBranchStepOrder={step.falseBranchStepOrder?.toString() || null}
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

ConditionalNode.displayName = 'ConditionalNode';
