'use client';

import { useState } from 'react';
import { MultiSelect, Text, Group, Loader } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useAuth } from '@/lib/hooks/use-auth';
import { useSkills } from '@/lib/hooks/use-skills';
import { setAgentSkills } from '@/lib/api/skills';

interface AgentSkillsSelectProps {
  agentId: string;
  assignedSkills: string[] | null;
  onUpdated: () => void;
}

export function AgentSkillsSelect({ agentId, assignedSkills, onUpdated }: AgentSkillsSelectProps) {
  const { isAdmin } = useAuth();
  const { data, isLoading } = useSkills();
  const [saving, setSaving] = useState(false);

  const value = assignedSkills ?? [];

  if (!isAdmin) {
    return (
      <Group gap="xs">
        <Text size="sm" fw={500}>Skills</Text>
        <Text size="sm" c="dimmed">{value.length > 0 ? value.join(', ') : 'None assigned'}</Text>
      </Group>
    );
  }

  if (isLoading) {
    return (
      <Group gap="xs">
        <Text size="sm" fw={500}>Skills</Text>
        <Loader size="xs" />
      </Group>
    );
  }

  const skillData = (data?.skills ?? []).map((s) => ({ value: s.name, label: s.displayName }));

  const handleChange = async (newValue: string[]) => {
    setSaving(true);
    try {
      await setAgentSkills(agentId, newValue);
      notifications.show({ title: 'Updated', message: 'Skills updated', color: 'green' });
      onUpdated();
    } catch (err: unknown) {
      notifications.show({
        title: 'Error',
        message: err instanceof Error ? err.message : 'Failed to update skills',
        color: 'red',
      });
    } finally {
      setSaving(false);
    }
  };

  return (
    <MultiSelect
      label="Skills"
      description="Which skills this agent can use."
      data={skillData}
      value={value}
      onChange={handleChange}
      disabled={saving}
      rightSection={saving ? <Loader size="xs" /> : undefined}
      maw={480}
    />
  );
}
