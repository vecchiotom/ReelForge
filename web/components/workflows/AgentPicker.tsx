'use client';

import { Select, Group, Text, Badge } from '@mantine/core';
import { useAgents } from '@/lib/hooks/use-agents';
import { getAgentGroup } from '@/lib/utils/constants';
import { useMemo } from 'react';
import type { ComboboxItem, SelectProps } from '@mantine/core';

interface AgentOptionItem extends ComboboxItem {
  toolCount: number;
  generatesOutput: boolean;
}

interface AgentPickerProps {
  value: string;
  onChange: (value: string) => void;
}

const renderOption: SelectProps['renderOption'] = ({ option }) => {
  const item = option as AgentOptionItem;
  return (
    <Group justify="space-between" wrap="nowrap" gap="xs" style={{ width: '100%' }}>
      <Text size="sm" truncate>{item.label}</Text>
      <Group gap={4} wrap="nowrap">
        {item.toolCount > 0 && (
          <Badge color="blue" variant="light" size="xs">{item.toolCount}t</Badge>
        )}
        {item.generatesOutput && (
          <Badge color="teal" variant="light" size="xs">out</Badge>
        )}
      </Group>
    </Group>
  );
};

export function AgentPicker({ value, onChange }: AgentPickerProps) {
  const { data: agents } = useAgents();

  const options = useMemo(() => {
    if (!agents) return [];
    const groups: Record<string, AgentOptionItem[]> = {};
    for (const agent of agents) {
      const group = getAgentGroup(agent.agentType);
      if (!groups[group]) groups[group] = [];
      groups[group].push({
        value: agent.id,
        label: agent.name,
        toolCount: agent.availableTools?.length ?? 0,
        generatesOutput: agent.generatesOutput,
      });
    }
    return Object.entries(groups).map(([group, items]) => ({ group, items }));
  }, [agents]);

  return (
    <Select
      placeholder="Select agent"
      data={options}
      value={value}
      onChange={(v) => v && onChange(v)}
      searchable
      renderOption={renderOption}
    />
  );
}
