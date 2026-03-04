'use client';

import { use, useState } from 'react';
import { Card, Text, Stack, Group, Button, Code, Loader, Center } from '@mantine/core';
import { IconEdit, IconTrash } from '@tabler/icons-react';
import { useAgent } from '@/lib/hooks/use-agents';
import { deleteAgent } from '@/lib/api/agents';
import { PageHeader } from '@/components/shared/PageHeader';
import { AgentTypeBadge } from '@/components/agents/AgentTypeBadge';
import { AgentForm } from '@/components/agents/AgentForm';
import { ConfirmModal } from '@/components/shared/ConfirmModal';
import { notifications } from '@mantine/notifications';
import { useRouter } from 'next/navigation';

export default function AgentDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const { data: agent, isLoading, mutate } = useAgent(id);
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
          <AgentTypeBadge category={agent.category} />
          {agent.isBuiltIn && <Text size="xs" c="dimmed">Built-in (read-only)</Text>}
        </Group>

        {agent.description && <Text>{agent.description}</Text>}

        <Card withBorder>
          <Text size="sm" fw={600} mb="xs">System Prompt</Text>
          <Code block style={{ whiteSpace: 'pre-wrap' }}>{agent.systemPrompt}</Code>
        </Card>
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
