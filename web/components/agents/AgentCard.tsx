'use client';

import { Card, Text, Group, Stack, ActionIcon, Tooltip, Collapse, Button } from '@mantine/core';
import { IconRobot, IconLock, IconExternalLink, IconChevronDown, IconChevronUp } from '@tabler/icons-react';
import { useRouter } from 'next/navigation';
import { useState } from 'react';
import { AgentTypeBadge } from './AgentTypeBadge';
import { AgentCapabilityBadge } from './AgentCapabilityBadge';
import { AgentSchemaViewer } from './AgentSchemaViewer';
import type { AgentDefinition } from '@/lib/types/agent';

export function AgentCard({ agent }: { agent: AgentDefinition }) {
  const router = useRouter();
  const [schemaOpen, setSchemaOpen] = useState(false);

  return (
    <Card shadow="sm" padding="lg" radius="md" withBorder>
      <Stack gap="sm">
        <Group justify="space-between" wrap="nowrap">
          <Group
            gap="xs"
            style={{ flex: 1, cursor: 'pointer' }}
            onClick={() => router.push(`/agents/${agent.id}`)}
          >
            <IconRobot size={20} />
            <Text fw={600} size="sm">{agent.name}</Text>
          </Group>
          <Group gap={4}>
            {agent.isBuiltIn && <IconLock size={16} color="gray" />}
            <Tooltip label="Open agent">
              <ActionIcon
                variant="subtle"
                size="sm"
                onClick={() => router.push(`/agents/${agent.id}`)}
              >
                <IconExternalLink size={16} />
              </ActionIcon>
            </Tooltip>
          </Group>
        </Group>

        <AgentTypeBadge agentType={agent.agentType} color={agent.color} />
        <AgentCapabilityBadge agent={agent} />

        {agent.description && (
          <Text size="xs" c="dimmed" lineClamp={2}>
            {agent.description}
          </Text>
        )}

        {agent.outputSchemaJson && (
          <>
            <Button
              variant="subtle"
              size="compact-xs"
              color="teal"
              leftSection={schemaOpen ? <IconChevronUp size={12} /> : <IconChevronDown size={12} />}
              onClick={() => setSchemaOpen((o) => !o)}
              justify="start"
            >
              {schemaOpen ? 'Hide' : 'Show'} output schema
            </Button>
            <Collapse in={schemaOpen}>
              <AgentSchemaViewer schemaJson={agent.outputSchemaJson} />
            </Collapse>
          </>
        )}
      </Stack>
    </Card>
  );
}
