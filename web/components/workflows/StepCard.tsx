'use client';

import { Card, Group, Text, ActionIcon, TextInput } from '@mantine/core';
import { IconGripVertical, IconTrash } from '@tabler/icons-react';
import { useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { AgentPicker } from './AgentPicker';

interface StepCardProps {
  id: string;
  label: string;
  agentDefinitionId: string;
  onLabelChange: (label: string) => void;
  onAgentChange: (agentId: string) => void;
  onRemove: () => void;
}

export function StepCard({ id, label, agentDefinitionId, onLabelChange, onAgentChange, onRemove }: StepCardProps) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({ id });

  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
    opacity: isDragging ? 0.5 : 1,
  };

  return (
    <Card ref={setNodeRef} style={style} withBorder padding="sm" radius="md">
      <Group gap="sm" wrap="nowrap">
        <ActionIcon variant="subtle" {...attributes} {...listeners} style={{ cursor: 'grab' }}>
          <IconGripVertical size={16} />
        </ActionIcon>
        <TextInput
          placeholder="Step label"
          value={label}
          onChange={(e) => onLabelChange(e.target.value)}
          style={{ flex: 1 }}
          size="sm"
        />
        <div style={{ flex: 1 }}>
          <AgentPicker value={agentDefinitionId} onChange={onAgentChange} />
        </div>
        <ActionIcon color="red" variant="subtle" onClick={onRemove}>
          <IconTrash size={16} />
        </ActionIcon>
      </Group>
    </Card>
  );
}
