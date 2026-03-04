'use client';

import { Select } from '@mantine/core';
import { useAgents } from '@/lib/hooks/use-agents';
import { useMemo } from 'react';

interface AgentPickerProps {
  value: string;
  onChange: (value: string) => void;
}

export function AgentPicker({ value, onChange }: AgentPickerProps) {
  const { data: agents } = useAgents();

  const options = useMemo(() => {
    if (!agents) return [];
    const groups: Record<string, { value: string; label: string }[]> = {};
    for (const agent of agents) {
      const cat = agent.category || 'Custom';
      if (!groups[cat]) groups[cat] = [];
      groups[cat].push({ value: agent.id, label: agent.name });
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
    />
  );
}
