'use client';

import { memo } from 'react';
import { Handle, Position } from '@xyflow/react';
import { Card, Group, Text, ActionIcon, Badge, Stack, TextInput, Tooltip, Collapse } from '@mantine/core';
import { IconTrash, IconUsersGroup, IconChevronDown, IconChevronUp } from '@tabler/icons-react';
import { motion } from 'framer-motion';
import { EditRoomStepConfigEditor } from '../EditRoomStepConfigEditor';
import type { StepData } from '../WorkflowStepList';

interface EditRoomNodeData {
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

/**
 * Canvas node for a StepType.EditRoom step — a multi-agent group-chat deliberation that emits the
 * exact same VideoEditDecisionOutput shape a solo VideoStoryEditor agent step would, so it slots
 * anywhere that step does. The step's own agent FK is the deterministic VideoTransform placeholder
 * (auto-assigned on add, same as VideoAnalyze/VideoCompile); the room's real seats/director are
 * resolved server-side from EditRoomConfigJson, so no agent picker is shown here.
 */
export const EditRoomNode = memo(({ data }: { data: EditRoomNodeData }) => {
  const { step, stepNumber, allSteps, currentStepIndex, expanded, pinned, onExpandChange, onTogglePin, onChange, onRemove } = data;
  const isOpen = expanded || pinned;

  return (
    <>
      <Handle type="target" position={Position.Top} style={{ background: '#ec4899' }} />

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
            border: '2px solid #ec4899',
            background: 'linear-gradient(135deg, light-dark(#ffffff, var(--mantine-color-dark-7)) 0%, light-dark(#fdf2f8, var(--mantine-color-dark-6)) 100%)',
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
                    background: 'linear-gradient(135deg, #ec4899 0%, #be185d 100%)',
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
                  gradient={{ from: 'pink', to: 'grape', deg: 90 }}
                  leftSection={<IconUsersGroup size={14} />}
                >
                  EDIT ROOM
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
              placeholder="Step label (e.g. Edit room deliberation)"
              value={step.label}
              onChange={(e) => onChange({ label: e.target.value })}
              size="sm"
              styles={{ input: { fontWeight: 600, fontSize: 15, border: 'none', background: 'transparent', padding: 0 } }}
              onClick={(e) => e.stopPropagation()}
            />

            <Text size="xs" c="dimmed">
              Several editor seats + a director deliberate over a VideoAnalyze view, then emit ONE
              editorial decision — the same shape a solo story-editor step produces.
            </Text>

            {/* Expanded: config form */}
            <Collapse in={isOpen}>
              <EditRoomStepConfigEditor
                configJson={step.editRoomConfigJson}
                onChange={(editRoomConfigJson) => onChange({ editRoomConfigJson })}
                allSteps={allSteps}
                currentStepIndex={currentStepIndex}
              />
            </Collapse>
          </Stack>
        </Card>
      </motion.div>

      <Handle type="source" position={Position.Bottom} style={{ background: '#ec4899' }} />
    </>
  );
});

EditRoomNode.displayName = 'EditRoomNode';
