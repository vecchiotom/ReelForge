'use client';

import { Badge, Group, Tooltip } from '@mantine/core';
import { IconTools, IconFileExport } from '@tabler/icons-react';
import type { AgentDefinition } from '@/lib/types/agent';

export function AgentCapabilityBadge({ agent }: { agent: AgentDefinition }) {
  const toolCount = agent.availableTools?.length ?? 0;

  return (
    <Group gap={6}>
      {toolCount > 0 && (
        <Tooltip
          label={agent.availableTools!.join(', ')}
          multiline
          maw={320}
          withArrow
        >
          <Badge
            color="blue"
            variant="outline"
            size="sm"
            leftSection={<IconTools size={10} />}
          >
            {toolCount} tool{toolCount !== 1 ? 's' : ''}
          </Badge>
        </Tooltip>
      )}
      {agent.generatesOutput && (
        <Tooltip
          label={agent.outputSchemaName ? `Output schema: ${agent.outputSchemaName}` : 'Generates structured output'}
          withArrow
        >
          <Badge
            color="teal"
            variant="outline"
            size="sm"
            leftSection={<IconFileExport size={10} />}
          >
            {agent.outputSchemaName ?? 'Output'}
          </Badge>
        </Tooltip>
      )}
    </Group>
  );
}
