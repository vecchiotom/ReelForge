'use client';

import { use, useState } from 'react';
import { Card, Text, Stack, Group, Button, Code, Loader, Center, Badge, SimpleGrid } from '@mantine/core';
import { IconEdit, IconTrash, IconTools, IconFileExport } from '@tabler/icons-react';
import { useAgent } from '@/lib/hooks/use-agents';
import { deleteAgent } from '@/lib/api/agents';
import { PageHeader } from '@/components/shared/PageHeader';
import { AgentTypeBadge } from '@/components/agents/AgentTypeBadge';
import { AgentSchemaViewer } from '@/components/agents/AgentSchemaViewer';
import { AgentForm } from '@/components/agents/AgentForm';
import { AgentInferenceProviderSelect } from '@/components/agents/AgentInferenceProviderSelect';
import { AgentSkillsSelect } from '@/components/agents/AgentSkillsSelect';
import { useSkills } from '@/lib/hooks/use-skills';
import { ConfirmModal } from '@/components/shared/ConfirmModal';
import { notifications } from '@mantine/notifications';
import { useRouter } from 'next/navigation';

export default function AgentDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const { data: agent, isLoading, mutate } = useAgent(id);
  const { data: skillsData } = useSkills();
  const router = useRouter();
  const [editOpened, setEditOpened] = useState(false);
  const [deleteOpened, setDeleteOpened] = useState(false);
  const [deleteLoading, setDeleteLoading] = useState(false);

  if (isLoading) return <Center h={300}><Loader /></Center>;
  if (!agent) return <Text>Agent not found</Text>;

  const handleDelete = async () => {
    setDeleteLoading(true);
    try {
      await deleteAgent(id);
      notifications.show({ title: 'Deleted', message: 'Agent deleted', color: 'green' });
      router.push('/agents');
    } catch {
      notifications.show({ title: 'Error', message: 'Failed to delete agent', color: 'red' });
    } finally {
      setDeleteLoading(false);
    }
  };

  return (
    <>
      <PageHeader
        title={agent.name}
        breadcrumbs={[{ label: 'Agents', href: '/agents' }, { label: agent.name }]}
      >
        {!agent.isBuiltIn && (
          <>
            <Button variant="default" leftSection={<IconEdit size={16} />} onClick={() => setEditOpened(true)}>
              Edit
            </Button>
            <Button color="red" variant="outline" leftSection={<IconTrash size={16} />} onClick={() => setDeleteOpened(true)}>
              Delete
            </Button>
          </>
        )}
      </PageHeader>

      <Stack gap="md">
        <Group>
          <AgentTypeBadge agentType={agent.agentType} color={agent.color} />
          {agent.isBuiltIn && <Text size="xs" c="dimmed">Built-in (read-only)</Text>}
          {agent.generatesOutput && (
            <Badge color="teal" variant="outline" size="sm" leftSection={<IconFileExport size={10} />}>
              {agent.outputSchemaName ?? 'Output'}
            </Badge>
          )}
        </Group>

        {agent.description && <Text>{agent.description}</Text>}

        <Card withBorder>
          <AgentInferenceProviderSelect
            agentId={agent.id}
            inferenceProviderId={agent.inferenceProviderId}
            inferenceProviderName={agent.inferenceProviderName}
            onUpdated={() => mutate()}
          />
        </Card>

        <Card withBorder>
          {agent.isBuiltIn ? (
            <Stack gap="xs">
              <Text fw={500} size="sm">Skills</Text>
              {agent.effectiveSkills.length > 0 ? (
                <Group gap="xs">
                  {agent.effectiveSkills.map((skillName) => {
                    const skill = skillsData?.find((s) => s.name === skillName);
                    return (
                      <Badge key={skillName} color="teal" variant="light" size="sm">
                        {skill?.displayName ?? skillName}
                      </Badge>
                    );
                  })}
                </Group>
              ) : (
                <Text size="sm" c="dimmed">This agent has no skills assigned.</Text>
              )}
              <Text size="xs" c="dimmed">Skills are fixed for built-in agents.</Text>
            </Stack>
          ) : (
            <AgentSkillsSelect
              agentId={agent.id}
              assignedSkills={agent.assignedSkills}
              onUpdated={() => mutate()}
            />
          )}
        </Card>

        {agent.availableTools && agent.availableTools.length > 0 && (
          <Card withBorder>
            <Text size="sm" fw={600} mb="xs">
              <Group gap="xs" display="inline-flex">
                <IconTools size={14} />
                Available Tools ({agent.availableTools.length})
              </Group>
            </Text>
            <SimpleGrid cols={{ base: 2, sm: 3, md: 4 }} spacing="xs">
              {agent.availableTools.map((tool) => (
                <Badge key={tool} color="blue" variant="light" size="sm" fullWidth style={{ justifyContent: 'flex-start' }}>
                  {tool}
                </Badge>
              ))}
            </SimpleGrid>
          </Card>
        )}

        <Card withBorder>
          <Text size="sm" fw={600} mb="xs">System Prompt</Text>
          <Code block style={{ whiteSpace: 'pre-wrap' }}>{agent.systemPrompt}</Code>
        </Card>

        {agent.outputSchemaJson && (
          <Card withBorder>
            <Text size="sm" fw={600} mb="xs">Structured Output Schema</Text>
            <Text size="xs" c="dimmed" mb="sm">
              This agent enforces output to conform to the following JSON schema:
            </Text>
            <AgentSchemaViewer schemaJson={agent.outputSchemaJson} />
          </Card>
        )}
      </Stack>

      {!agent.isBuiltIn && (
        <>
          <AgentForm opened={editOpened} onClose={() => setEditOpened(false)} onSuccess={() => mutate()} agent={agent} />
          <ConfirmModal
            opened={deleteOpened}
            onClose={() => setDeleteOpened(false)}
            onConfirm={handleDelete}
            title="Delete Agent"
            message="This will permanently delete this custom agent."
            loading={deleteLoading}
          />
        </>
      )}
    </>
  );
}
