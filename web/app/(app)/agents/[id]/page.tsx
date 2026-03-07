'use client';

import { use, useState } from 'react';
import { Card, Text, Stack, Group, Button, Code, Loader, Center, Box, Badge, SimpleGrid } from '@mantine/core';
import { IconEdit, IconTrash, IconTools, IconFileExport } from '@tabler/icons-react';
import { Prism as SyntaxHighlighter } from 'react-syntax-highlighter';
import { oneDark } from 'react-syntax-highlighter/dist/esm/styles/prism';
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
          <AgentTypeBadge agentType={agent.agentType} color={agent.color} />
          {agent.isBuiltIn && <Text size="xs" c="dimmed">Built-in (read-only)</Text>}
          {agent.generatesOutput && (
            <Badge color="teal" variant="outline" size="sm" leftSection={<IconFileExport size={10} />}>
              {agent.outputSchemaName ?? 'Output'}
            </Badge>
          )}
        </Group>

        {agent.description && <Text>{agent.description}</Text>}

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
            <Box style={{
              maxHeight: '500px',
              overflow: 'auto',
              borderRadius: '8px',
              fontSize: '13px'
            }}>
              <SyntaxHighlighter
                language="json"
                style={oneDark}
                customStyle={{
                  margin: 0,
                  borderRadius: '8px',
                  padding: '16px'
                }}
                showLineNumbers
              >
                {JSON.stringify(JSON.parse(agent.outputSchemaJson), null, 2)}
              </SyntaxHighlighter>
            </Box>
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
