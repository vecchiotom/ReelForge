'use client';

import { Card, Text, Group, Stack, Badge, ActionIcon, Tooltip } from '@mantine/core';
import { IconBook, IconExternalLink, IconBrandGithub } from '@tabler/icons-react';
import { useRouter } from 'next/navigation';
import type { Skill } from '@/lib/types/skill';

function categoryColor(category: Skill['category']) {
  switch (category) {
    case 'Remotion':
      return 'indigo';
    case 'ReelForge':
      return 'teal';
    default:
      return 'grape';
  }
}

export function SkillCard({ skill }: { skill: Skill }) {
  const router = useRouter();

  return (
    <Card shadow="sm" padding="lg" radius="md" withBorder>
      <Stack gap="sm">
        <Group justify="space-between" wrap="nowrap">
          <Group
            gap="xs"
            style={{ flex: 1, cursor: 'pointer' }}
            onClick={() => router.push(`/admin/skills/${encodeURIComponent(skill.name)}`)}
          >
            <IconBook size={20} />
            <Text fw={600} size="sm">{skill.displayName}</Text>
          </Group>
          <Tooltip label="Open skill">
            <ActionIcon
              variant="subtle"
              size="sm"
              onClick={() => router.push(`/admin/skills/${encodeURIComponent(skill.name)}`)}
            >
              <IconExternalLink size={16} />
            </ActionIcon>
          </Tooltip>
        </Group>

        <Group gap="xs">
          <Badge color={categoryColor(skill.category)} variant="light" size="sm">
            {skill.category}
          </Badge>
          {skill.version && (
            <Badge color="gray" variant="outline" size="sm">
              v{skill.version}
            </Badge>
          )}
          {skill.sourceUrl && (
            <Tooltip label="Vendored from GitHub — view source">
              <ActionIcon
                component="a"
                href={skill.sourceUrl}
                target="_blank"
                rel="noopener noreferrer"
                variant="subtle"
                color="gray"
                size="sm"
                onClick={(e) => e.stopPropagation()}
                aria-label="View skill source on GitHub"
              >
                <IconBrandGithub size={14} />
              </ActionIcon>
            </Tooltip>
          )}
        </Group>

        {skill.description && (
          <Text size="xs" c="dimmed" lineClamp={2}>
            {skill.description}
          </Text>
        )}

        <Text size="xs" c="dimmed">
          {skill.defaultForAgentTypes.length > 0
            ? `Used by ${skill.defaultForAgentTypes.length} agent type${skill.defaultForAgentTypes.length === 1 ? '' : 's'}`
            : 'Not used by any built-in agent type'}
          {skill.assignedAgentCount > 0 ? ` · +${skill.assignedAgentCount} custom` : ''}
        </Text>
      </Stack>
    </Card>
  );
}
