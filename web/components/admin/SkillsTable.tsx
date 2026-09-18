'use client';

import { Table, Badge, Text } from '@mantine/core';
import { useRouter } from 'next/navigation';
import type { Skill } from '@/lib/types/skill';

interface SkillsTableProps {
  skills: Skill[];
}

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

function usageSummary(skill: Skill) {
  const parts: string[] = [];
  if (skill.defaultForAgentTypes.length > 0) {
    parts.push(`Used by ${skill.defaultForAgentTypes.length} agent type${skill.defaultForAgentTypes.length === 1 ? '' : 's'}`);
  }
  if (skill.assignedAgentCount > 0) {
    parts.push(`+${skill.assignedAgentCount} custom`);
  }
  return parts.length > 0 ? parts.join(' ') : 'Not currently used';
}

export function SkillsTable({ skills }: SkillsTableProps) {
  const router = useRouter();

  return (
    <Table.ScrollContainer minWidth={700}>
      <Table striped highlightOnHover>
        <Table.Thead>
          <Table.Tr>
            <Table.Th>Name</Table.Th>
            <Table.Th>Description</Table.Th>
            <Table.Th>Category</Table.Th>
            <Table.Th visibleFrom="sm">Version</Table.Th>
            <Table.Th visibleFrom="sm">Usage</Table.Th>
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {skills.map((skill) => (
            <Table.Tr
              key={skill.name}
              onClick={() => router.push(`/admin/skills/${encodeURIComponent(skill.name)}`)}
              style={{ cursor: 'pointer' }}
            >
              <Table.Td>
                <Text fw={500} size="sm">{skill.displayName}</Text>
                <Text size="xs" c="dimmed">{skill.name}</Text>
              </Table.Td>
              <Table.Td>
                <Text size="sm" lineClamp={2} maw={420}>{skill.description}</Text>
              </Table.Td>
              <Table.Td>
                <Badge color={categoryColor(skill.category)} variant="light" size="sm">
                  {skill.category}
                </Badge>
              </Table.Td>
              <Table.Td visibleFrom="sm">{skill.version ?? '—'}</Table.Td>
              <Table.Td visibleFrom="sm">
                <Text size="xs" c="dimmed">{usageSummary(skill)}</Text>
              </Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
    </Table.ScrollContainer>
  );
}
