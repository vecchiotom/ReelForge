'use client';

import { use } from 'react';
import { Card, Text, Stack, Group, Badge, Loader, Center, Code } from '@mantine/core';
import { IconBook } from '@tabler/icons-react';
import { useSkill } from '@/lib/hooks/use-skills';
import { PageHeader } from '@/components/shared/PageHeader';

function categoryColor(category: string) {
  switch (category) {
    case 'Remotion':
      return 'indigo';
    case 'ReelForge':
      return 'teal';
    default:
      return 'grape';
  }
}

export default function SkillDetailPage({ params }: { params: Promise<{ name: string }> }) {
  const { name } = use(params);
  const { data: skill, isLoading } = useSkill(decodeURIComponent(name));

  if (isLoading) return <Center h={300}><Loader /></Center>;
  if (!skill) return <Text>Skill not found</Text>;

  return (
    <>
      <PageHeader
        title={skill.displayName}
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Skills', href: '/admin/skills' },
          { label: skill.displayName },
        ]}
      />

      <Stack gap="md">
        <Card withBorder>
          <Group gap="xs" mb="xs">
            <IconBook size={18} />
            <Text fw={600}>{skill.name}</Text>
          </Group>
          <Group gap="xs" mb="sm">
            <Badge color={categoryColor(skill.category)} variant="light" size="sm">
              {skill.category}
            </Badge>
            {skill.version && (
              <Badge color="gray" variant="outline" size="sm">
                v{skill.version}
              </Badge>
            )}
          </Group>
          {skill.description && <Text size="sm">{skill.description}</Text>}
          <Text size="xs" c="dimmed" mt="sm">
            {skill.defaultForAgentTypes.length > 0
              ? `Default for: ${skill.defaultForAgentTypes.join(', ')}`
              : 'Not a default skill for any built-in agent type.'}
          </Text>
          <Text size="xs" c="dimmed">
            Assigned to {skill.assignedAgentCount} custom agent{skill.assignedAgentCount === 1 ? '' : 's'}.
          </Text>
        </Card>

        <Card withBorder>
          <Text size="sm" fw={600} mb="xs">SKILL.md</Text>
          <Code block style={{ whiteSpace: 'pre-wrap' }}>{skill.body}</Code>
        </Card>
      </Stack>
    </>
  );
}
