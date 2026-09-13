'use client';

import { useState } from 'react';
import { Select, Text, Group, Loader } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useAuth } from '@/lib/hooks/use-auth';
import { useInferenceProviders } from '@/lib/hooks/use-inference-providers';
import { setAgentInferenceProvider } from '@/lib/api/agents';

interface AgentInferenceProviderSelectProps {
  agentId: string;
  inferenceProviderId: string | null;
  inferenceProviderName: string | null;
  onUpdated: () => void;
}

const DEFAULT_VALUE = '__default__';

export function AgentInferenceProviderSelect({
  agentId,
  inferenceProviderId,
  inferenceProviderName,
  onUpdated,
}: AgentInferenceProviderSelectProps) {
  const { isAdmin } = useAuth();
  const { data: providers, isLoading } = useInferenceProviders();
  const [saving, setSaving] = useState(false);

  if (!isAdmin) {
    return (
      <Group gap="xs">
        <Text size="sm" fw={500}>Inference Provider</Text>
        <Text size="sm" c="dimmed">{inferenceProviderName ?? 'Default provider'}</Text>
      </Group>
    );
  }

  if (isLoading) {
    return (
      <Group gap="xs">
        <Text size="sm" fw={500}>Inference Provider</Text>
        <Loader size="xs" />
      </Group>
    );
  }

  // This override feeds IAgentChatClientProvider (chat resolution only) — a Transcription-
  // capability provider must never be selectable here, since it has no chat completions
  // endpoint (e.g. a whisper.cpp-server/faster-whisper-server deployment).
  const data = [
    { value: DEFAULT_VALUE, label: 'Default provider' },
    ...(providers ?? [])
      .filter((p) => p.capability !== 'Transcription')
      .map((p) => ({ value: p.id, label: p.name })),
  ];

  const handleChange = async (value: string | null) => {
    const nextId = !value || value === DEFAULT_VALUE ? null : value;
    setSaving(true);
    try {
      await setAgentInferenceProvider(agentId, nextId);
      notifications.show({ title: 'Updated', message: 'Inference provider updated', color: 'green' });
      onUpdated();
    } catch (err: unknown) {
      notifications.show({
        title: 'Error',
        message: err instanceof Error ? err.message : 'Failed to update inference provider',
        color: 'red',
      });
    } finally {
      setSaving(false);
    }
  };

  return (
    <Select
      label="Inference Provider"
      description="Overrides the global default provider for this agent."
      data={data}
      value={inferenceProviderId ?? DEFAULT_VALUE}
      onChange={handleChange}
      disabled={saving}
      rightSection={saving ? <Loader size="xs" /> : undefined}
      allowDeselect={false}
      maw={360}
    />
  );
}
