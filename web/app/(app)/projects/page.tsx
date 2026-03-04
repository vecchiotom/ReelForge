'use client';

import { useState } from 'react';
import { SimpleGrid, Button, Loader, Center } from '@mantine/core';
import { IconPlus } from '@tabler/icons-react';
import { useProjects } from '@/lib/hooks/use-projects';
import { PageHeader } from '@/components/shared/PageHeader';
import { ProjectCard } from '@/components/projects/ProjectCard';
import { ProjectForm } from '@/components/projects/ProjectForm';
import { EmptyState } from '@/components/shared/EmptyState';

export default function ProjectsPage() {
  const { data: projects, isLoading, mutate } = useProjects();
  const [formOpened, setFormOpened] = useState(false);

  if (isLoading) {
    return <Center h={300}><Loader /></Center>;
  }

  return (
    <>
      <PageHeader title="Projects">
        <Button leftSection={<IconPlus size={16} />} onClick={() => setFormOpened(true)}>
          New Project
        </Button>
      </PageHeader>

      {projects && projects.length > 0 ? (
        <SimpleGrid cols={{ base: 1, sm: 2, lg: 3 }}>
          {projects.map((project) => (
            <ProjectCard key={project.id} project={project} />
          ))}
        </SimpleGrid>
      ) : (
        <EmptyState title="No projects" description="Create your first project to get started.">
          <Button onClick={() => setFormOpened(true)}>Create Project</Button>
        </EmptyState>
      )}

      <ProjectForm opened={formOpened} onClose={() => setFormOpened(false)} onSuccess={() => mutate()} />
    </>
  );
}
