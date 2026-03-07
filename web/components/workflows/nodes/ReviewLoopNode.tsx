'use client';

import { memo, useState } from 'react';
import { Handle, Position } from '@xyflow/react';
import { Card, Group, Text, ActionIcon, Badge, Stack, NumberInput, Tooltip, Modal, Button } from '@mantine/core';
import { IconTrash, IconStarFilled, IconSettings } from '@tabler/icons-react';
import { motion } from 'framer-motion';
import { ReviewLoopStepConfig } from '../ReviewLoopStepConfig';
import type { StepData } from '../WorkflowStepList';

interface ReviewLoopNodeData {
  step: StepData;
  stepNumber: number;
  allSteps: StepData[];
  currentStepIndex: number;
  onChange: (updates: Partial<StepData>) => void;
  onRemove: () => void;
}

export const ReviewLoopNode = memo(({ data }: { data: ReviewLoopNodeData }) => {
  const { step, stepNumber, allSteps, currentStepIndex, onChange, onRemove } = data;
  const [expanded, setExpanded] = useState(false);
  const [configModalOpen, setConfigModalOpen] = useState(false);

  return (
    <>
      <Handle type="target" position={Position.Top} style={{ background: '#ec4899' }} />
      
      <motion.div
        initial={{ scale: 0.8, opacity: 0 }}
        animate={{ scale: 1, opacity: 1 }}
        transition={{ duration: 0.3 }}
        whileHover={{ scale: 1.02 }}
      >
        <Card
          shadow="lg"
          padding="md"
          radius="lg"
          style={{
            width: 320,
            border: '2px solid #ec4899',
            background: 'linear-gradient(135deg, #ffffff 0%, #fdf2f8 100%)',
            cursor: 'pointer',
            boxShadow: '0 8px 16px rgba(236, 72, 153, 0.2)',
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
                    width: 36,
                    height: 36,
                    borderRadius: '50%',
                    background: 'linear-gradient(135deg, #ec4899 0%, #db2777 100%)',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    color: 'white',
                    fontWeight: 700,
                    fontSize: 14,
                    boxShadow: '0 4px 8px rgba(236, 72, 153, 0.3)',
                  }}
                >
                  {stepNumber}
                </div>
                <Badge
                  size="lg"
                  variant="gradient"
                  gradient={{ from: 'pink', to: 'red', deg: 90 }}
                  leftSection={<IconStarFilled size={14} />}
                  style={{ fontWeight: 700 }}
                >
                  REVIEW LOOP
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
            <Text fw={700} size="sm" c="pink.7">
              {step.label || 'Quality Review Gate'}
            </Text>

            {/* Quick Stats */}
            <Group grow>
              <div
                style={{
                  background: 'linear-gradient(135deg, #fce7f3 0%, #fbcfe8 100%)',
                  padding: 8,
                  borderRadius: 6,
                  textAlign: 'center',
                }}
              >
                <Text size="xs" c="dimmed">Min Score</Text>
                <Text size="lg" fw={700} c="pink.7">{step.minScore || 9}/10</Text>
              </div>
              <div
                style={{
                  background: 'linear-gradient(135deg, #fce7f3 0%, #fbcfe8 100%)',
                  padding: 8,
                  borderRadius: 6,
                  textAlign: 'center',
                }}
              >
                <Text size="xs" c="dimmed">Max Loops</Text>
                <Text size="lg" fw={700} c="pink.7">{step.maxIterations || 3}</Text>
              </div>
            </Group>

            {/* Expanded Content */}
            <motion.div
              initial={false}
              animate={{ height: expanded ? 'auto' : 0, opacity: expanded ? 1 : 0 }}
              transition={{ duration: 0.2 }}
              style={{ overflow: 'hidden' }}
            >
              <Stack gap="xs">
                <NumberInput
                  label="Minimum Score (1-10)"
                  value={step.minScore || 9}
                  onChange={(value) => onChange({ minScore: Number(value) || 9 })}
                  min={1}
                  max={10}
                  size="xs"
                  onClick={(e) => e.stopPropagation()}
                />
                
                <NumberInput
                  label="Max Iterations"
                  value={step.maxIterations}
                  onChange={(value) => onChange({ maxIterations: Number(value) || 3 })}
                  min={1}
                  max={10}
                  size="xs"
                  onClick={(e) => e.stopPropagation()}
                />

                <NumberInput
                  label="Loop Back to Step"
                  value={step.loopTargetStepOrder || 1}
                  onChange={(value) => onChange({ loopTargetStepOrder: Number(value) || 1 })}
                  min={1}
                  max={stepNumber - 1}
                  size="xs"
                  onClick={(e) => e.stopPropagation()}
                />

                <div
                  style={{
                    background: 'linear-gradient(135deg, #dcfce7 0%, #bbf7d0 100%)',
                    padding: 10,
                    borderRadius: 6,
                    border: '1px solid #86efac',
                  }}
                >
                  <Text size="xs" fw={600} c="green.8">
                    ✓ Score ≥ {step.minScore || 9} → Continue
                  </Text>
                </div>

                <div
                  style={{
                    background: 'linear-gradient(135deg, #fed7aa 0%, #fdba74 100%)',
                    padding: 10,
                    borderRadius: 6,
                    border: '1px solid #fb923c',
                  }}
                >
                  <Text size="xs" fw={600} c="orange.8">
                    ↻ Score &lt; {step.minScore || 9} → Loop to Step {step.loopTargetStepOrder || 1}
                  </Text>
                </div>
              </Stack>
            </motion.div>
          </Stack>
        </Card>
      </motion.div>

      <Handle type="source" position={Position.Bottom} id="continue" style={{ left: '25%', background: '#10b981' }} />
      <Handle type="source" position={Position.Bottom} id="loop" style={{ left: '75%', background: '#f59e0b' }} />

      {/* Configuration Modal with Schema Viewer */}
      <Modal
        opened={configModalOpen}
        onClose={() => setConfigModalOpen(false)}
        title={<Text fw={700}>Configure Review Loop Step</Text>}
        size="xl"
        onClick={(e) => e.stopPropagation()}
      >
        <ReviewLoopStepConfig
          minScore={step.minScore}
          maxIterations={step.maxIterations}
          loopTargetStepOrder={step.loopTargetStepOrder}
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

ReviewLoopNode.displayName = 'ReviewLoopNode';
