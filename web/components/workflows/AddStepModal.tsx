'use client';

import { Modal, Stack, Button, Text } from '@mantine/core';
import { IconRobot, IconGitBranch, IconRepeat, IconStarFilled, IconLayoutColumns, IconFilterCog, IconWaveSine, IconScissors } from '@tabler/icons-react';
import type { StepType } from '@/lib/types/workflow';

interface AddStepModalProps {
  opened: boolean;
  onClose: () => void;
  onAdd: (stepType: StepType) => void;
}

export function AddStepModal({ opened, onClose, onAdd }: AddStepModalProps) {
  const stepTypes: { type: StepType; label: string; icon: React.ReactNode; color: string; description: string }[] = [
    {
      type: 'Agent',
      label: 'Agent Step',
      icon: <IconRobot size={24} />,
      color: 'violet',
      description: 'Execute an AI agent with a specific prompt and tools',
    },
    {
      type: 'Conditional',
      label: 'Conditional Branch',
      icon: <IconGitBranch size={24} />,
      color: 'yellow',
      description: 'Branch workflow based on a condition expression',
    },
    {
      type: 'ForEach',
      label: 'For Each Loop',
      icon: <IconRepeat size={24} />,
      color: 'cyan',
      description: 'Iterate over a collection of items',
    },
    {
      type: 'ReviewLoop',
      label: 'Review Loop',
      icon: <IconStarFilled size={24} />,
      color: 'pink',
      description: 'Quality gate: loop back until minimum score is achieved',
    },
    {
      type: 'Parallel',
      label: 'Parallel Step',
      icon: <IconLayoutColumns size={24} />,
      color: 'teal',
      description: 'Run multiple agents in parallel and merge their outputs',
    },
    {
      type: 'Extract',
      label: 'Extract Step',
      icon: <IconFilterCog size={24} />,
      color: 'grape',
      description: 'Deterministically reduce data before an AI step — no model call',
    },
    {
      type: 'VideoAnalyze',
      label: 'Analyze Video',
      icon: <IconWaveSine size={24} />,
      color: 'blue',
      description: 'Deterministic ffmpeg-based derushing — silence, shot, and transcript analysis',
    },
    {
      type: 'VideoCompile',
      label: 'Compile Video',
      icon: <IconScissors size={24} />,
      color: 'indigo',
      description: 'Deterministic ffmpeg-based cutting from an editorial decision',
    },
  ];

  return (
    <Modal opened={opened} onClose={onClose} title="Add Workflow Step" size="lg" centered>
      <Stack gap="md">
        <Text size="sm" c="dimmed">
          Choose a step type to add to your workflow
        </Text>
        
        <Stack gap="sm">
          {stepTypes.map((st) => (
            <Button
              key={st.type}
              variant="light"
              color={st.color}
              size="lg"
              leftSection={st.icon}
              onClick={() => onAdd(st.type)}
              styles={{
                root: { height: 'auto', padding: '16px' },
                inner: { justifyContent: 'flex-start' },
              }}
            >
              <div style={{ textAlign: 'left' }}>
                <Text fw={600}>{st.label}</Text>
                <Text size="xs" c="dimmed" fw={400}>{st.description}</Text>
              </div>
            </Button>
          ))}
        </Stack>
      </Stack>
    </Modal>
  );
}
