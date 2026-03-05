'use client';

import { useState } from 'react';
import { Modal, TextInput, Textarea, Button, Stack, ColorInput } from '@mantine/core';
import { useForm } from '@mantine/form';
import { notifications } from '@mantine/notifications';
import { createAgent, updateAgent } from '@/lib/api/agents';
import type { AgentDefinition } from '@/lib/types/agent';

interface AgentFormProps {
  opened: boolean;
  onClose: () => void;
  onSuccess: () => void;
  agent?: AgentDefinition;
}

export function AgentForm({ opened, onClose, onSuccess, agent }: AgentFormProps) {
  const [loading, setLoading] = useState(false);
  const isEdit = !!agent;

  const form = useForm({
    initialValues: {
      name: agent?.name || '',
      description: agent?.description || '',
      systemPrompt: agent?.systemPrompt || '',
      color: agent?.color || '',
    },
    validate: {
      name: (v) => (!v.trim() ? 'Name is required' : null),
      systemPrompt: (v) => (!v.trim() ? 'System prompt is required' : null),
    },
  });

  const handleSubmit = form.onSubmit(async (values) => {
    setLoading(true);
    try {
      if (isEdit) {
        await updateAgent(agent.id, values);
        notifications.show({ title: 'Updated', message: 'Agent updated', color: 'green' });
      } else {
        await createAgent(values);
        notifications.show({ title: 'Created', message: 'Agent created', color: 'green' });
      }
      form.reset();
      onSuccess();
      onClose();
    } catch (err: unknown) {
      notifications.show({
        title: 'Error',
        message: err instanceof Error ? err.message : 'Operation failed',
        color: 'red',
      });
    } finally {
      setLoading(false);
    }
  });

  return (
    <Modal opened={opened} onClose={onClose} title={isEdit ? 'Edit Agent' : 'Create Custom Agent'} centered size="lg">
      <form onSubmit={handleSubmit}>
        <Stack>
          <TextInput label="Name" placeholder="My Custom Agent" {...form.getInputProps('name')} />
          <Textarea label="Description" placeholder="What does this agent do?" rows={2} {...form.getInputProps('description')} />
          <Textarea label="System Prompt" placeholder="You are a..." rows={8} {...form.getInputProps('systemPrompt')} />
          <ColorInput label="Color" placeholder="#3B82F6" {...form.getInputProps('color')} />
          <Button type="submit" loading={loading} fullWidth>
            {isEdit ? 'Update' : 'Create'}
          </Button>
        </Stack>
      </form>
    </Modal>
  );
}
