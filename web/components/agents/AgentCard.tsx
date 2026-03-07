'use client';

import { Card, Text, Group, Stack } from '@mantine/core';
import { IconRobot, IconLock } from '@tabler/icons-react';
import Link from 'next/link';
import { AgentTypeBadge } from './AgentTypeBadge';
import { AgentCapabilityBadge } from './AgentCapabilityBadge';
import type { AgentDefinition } from '@/lib/types/agent';

export function AgentCard({ agent }: { agent: AgentDefinition }) {
  return (
    <Card
      component={Link}
      href={`/agents/${agent.id}`}
      shadow="sm"
      padding="lg"
      radius="md"
      withBorder
      style={{ textDecoration: 'none', cursor: 'pointer' }}
    >
      <Stack gap="sm">
        <Group justify="space-between">
          <Group gap="xs">
            <IconRobot size={20} />
            <Text fw={600} size="sm">{agent.name}</Text>
          </Group>
          {agent.isBuiltIn && <IconLock size={16} color="gray" />}
        </Group>
        <AgentTypeBadge agentType={agent.agentType} color={agent.color} />
        <AgentCapabilityBadge agent={agent} />
        {agent.description && (
          <Text size="xs" c="dimmed" lineClamp={2}>
            {agent.description}
          </Text>
        )}
      </Stack>
    </Card>
  );
}
