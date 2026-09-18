'use client';

import { use } from 'react';
import { Card, Text, Stack, Group, Badge, Loader, Center, Code, Anchor } from '@mantine/core';
import { IconBook, IconBrandGithub } from '@tabler/icons-react';
import { useSkill } from '@/lib/hooks/use-skills';
import { PageHeader } from '@/components/shared/PageHeader';

/** repo path portion of a GitHub URL, e.g. "remotion-dev/skills" from "https://github.com/remotion-dev/skills". */
function repoPathFromUrl(url: string): string | null {
  try {
    const parsed = new URL(url);
    if (parsed.hostname !== 'github.com') return null;
    const segments = parsed.pathname.split('/').filter(Boolean);
    return segments.length >= 2 ? `${segments[0]}/${segments[1]}` : null;
  } catch {
    return null;
  }
}

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

          <Group gap={6} mt="sm">
            <IconBrandGithub size={16} style={{ flexShrink: 0 }} />
            {skill.sourceUrl ? (
              (() => {
                const repoPath = repoPathFromUrl(skill.sourceUrl);
                return (
                  <Text size="sm">
                    Source:{' '}
                    <Anchor href={skill.sourceUrl} target="_blank" rel="noopener noreferrer" size="sm">
                      {repoPath ? `github.com/${repoPath}` : skill.sourceUrl}
                    </Anchor>
                    {skill.sourceCommit && (
                      <>
                        {' '}at commit{' '}
                        {repoPath ? (
                          <Anchor
                            href={`https://github.com/${repoPath}/commit/${skill.sourceCommit}`}
                            target="_blank"
                            rel="noopener noreferrer"
                            size="sm"
                            title={skill.sourceCommit}
                          >
                            <Code>{skill.sourceCommit.slice(0, 7)}</Code>
                          </Anchor>
                        ) : (
                          <Code title={skill.sourceCommit}>{skill.sourceCommit.slice(0, 7)}</Code>
                        )}
                      </>
                    )}
                  </Text>
                );
              })()
            ) : (
              <Text size="sm" c="dimmed">
                Authored in this repository — no external source.
              </Text>
            )}
          </Group>

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
