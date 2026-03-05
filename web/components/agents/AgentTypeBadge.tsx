'use client';

import { Badge, Box, Group } from '@mantine/core';
import { AGENT_GROUP_COLORS, getAgentGroup } from '@/lib/utils/constants';

export function AgentTypeBadge({ agentType, color }: { agentType: string; color?: string | null }) {
  const group = getAgentGroup(agentType);
  return (
    <Group gap={6}>
      <Badge color={AGENT_GROUP_COLORS[group] || 'gray'} variant="light" size="sm">
        {agentType}
      </Badge>
      {color && (
        <Box
          w={10}
          h={10}
          style={{ backgroundColor: color, borderRadius: '50%', flexShrink: 0 }}
        />
      )}
    </Group>
  );
}
