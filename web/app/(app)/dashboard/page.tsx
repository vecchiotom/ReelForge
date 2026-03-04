'use client';

import { SimpleGrid, Card, Text, Group, Stack, Loader, Center } from '@mantine/core';
import { IconFolder, IconRobot, IconPlayerPlay } from '@tabler/icons-react';
import { useProjects } from '@/lib/hooks/use-projects';
import { useAgents } from '@/lib/hooks/use-agents';
import { PageHeader } from '@/components/shared/PageHeader';
import { ProjectCard } from '@/components/projects/ProjectCard';

export default function DashboardPage() {
  const { data: projects, isLoading: loadingProjects } = useProjects();
  const { data: agents, isLoading: loadingAgents } = useAgents();

  if (loadingProjects || loadingAgents) {
    return <Center h={300}><Loader /></Center>;
  }

  const stats = [
    { label: 'Projects', value: projects?.length || 0, icon: IconFolder, color: 'violet' },
    { label: 'Agents', value: agents?.length || 0, icon: IconRobot, color: 'blue' },
    { label: 'Built-in Agents', value: agents?.filter((a) => a.isBuiltIn).length || 0, icon: IconPlayerPlay, color: 'teal' },
  ];

  return (
    <>
      <PageHeader title="Dashboard" />
      <SimpleGrid cols={{ base: 1, sm: 3 }} mb="xl">
        {stats.map((stat) => (
          <Card key={stat.label} shadow="sm" padding="lg" radius="md" withBorder>
            <Group>
              <stat.icon size={28} color={`var(--mantine-color-${stat.color}-6)`} />
              <div>
                <Text size="xs" c="dimmed" tt="uppercase" fw={700}>
                  {stat.label}
                </Text>
                <Text size="xl" fw={700}>
                  {stat.value}
                </Text>
              </div>
            </Group>
          </Card>
        ))}
      </SimpleGrid>

      <Text fw={600} size="lg" mb="sm">Recent Projects</Text>
      {projects && projects.length > 0 ? (
        <SimpleGrid cols={{ base: 1, sm: 2, lg: 3 }}>
          {projects.slice(0, 6).map((project) => (
            <ProjectCard key={project.id} project={project} />
          ))}
        </SimpleGrid>
      ) : (
        <Text c="dimmed">No projects yet. Create one from the Projects page.</Text>
      )}
    </>
  );
}
