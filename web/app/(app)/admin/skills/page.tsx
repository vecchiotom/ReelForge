'use client';

import { Loader, Center, Text } from '@mantine/core';
import { useSkills } from '@/lib/hooks/use-skills';
import { PageHeader } from '@/components/shared/PageHeader';
import { SkillsTable } from '@/components/admin/SkillsTable';
import { EmptyState } from '@/components/shared/EmptyState';

export default function SkillsPage() {
  const { data: skills, isLoading } = useSkills();

  if (isLoading) {
    return <Center h={300}><Loader /></Center>;
  }

  return (
    <>
      <PageHeader
        title="Skills"
        breadcrumbs={[{ label: 'Admin', href: '/admin' }, { label: 'Skills' }]}
      />

      <Text size="sm" c="dimmed" mb="md" maw={720}>
        Skills are defined in code and vendored documentation, not created here. Assign them to a
        custom agent from that agent&apos;s detail page — a built-in agent&apos;s skills are fixed.
      </Text>

      {skills && skills.length > 0 ? (
        <SkillsTable skills={skills} />
      ) : (
        <EmptyState
          title="No skills found"
          description="No skills are currently registered in the skill catalog."
        />
      )}
    </>
  );
}
