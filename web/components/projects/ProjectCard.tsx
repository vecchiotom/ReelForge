'use client';

import { Card, Text, Group, Stack } from '@mantine/core';
import { IconFolder } from '@tabler/icons-react';
import Link from 'next/link';
import { formatDate } from '@/lib/utils/format';
import type { Project } from '@/lib/types/project';

export function ProjectCard({ project }: { project: Project }) {
  return (
    <Card
      component={Link}
      href={`/projects/${project.id}`}
      shadow="sm"
      padding="lg"
      radius="md"
      withBorder
      style={{ textDecoration: 'none', cursor: 'pointer' }}
    >
      <Stack gap="sm">
        <Group>
          <IconFolder size={20} />
          <Text fw={600}>{project.name}</Text>
        </Group>
        {project.description && (
          <Text size="sm" c="dimmed" lineClamp={2}>
            {project.description}
          </Text>
        )}
        <Text size="xs" c="dimmed">
          Created {formatDate(project.createdAt)}
        </Text>
      </Stack>
    </Card>
  );
}
