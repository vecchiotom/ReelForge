'use client';

import { use, useState } from 'react';
import { Card, Text, Stack, Group, Badge, Button, Loader, Center } from '@mantine/core';
import { IconEdit, IconTrash, IconPlugConnected } from '@tabler/icons-react';
import { useInferenceProvider } from '@/lib/hooks/use-inference-providers';
import { deleteProvider, testProvider } from '@/lib/api/inference-providers';
import { PageHeader } from '@/components/shared/PageHeader';
import { InferenceProviderForm } from '@/components/admin/InferenceProviderForm';
import { ConfirmModal } from '@/components/shared/ConfirmModal';
import { formatDate } from '@/lib/utils/format';
import { notifications } from '@mantine/notifications';
import { useRouter } from 'next/navigation';

export default function InferenceProviderDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const { data: provider, isLoading, mutate } = useInferenceProvider(id);
  const router = useRouter();
  const [editOpened, setEditOpened] = useState(false);
  const [deleteOpened, setDeleteOpened] = useState(false);
  const [deleteLoading, setDeleteLoading] = useState(false);
  const [testLoading, setTestLoading] = useState(false);

  if (isLoading) return <Center h={300}><Loader /></Center>;
  if (!provider) return <Text>Provider not found</Text>;

  const handleDelete = async () => {
    setDeleteLoading(true);
    try {
      await deleteProvider(id);
      notifications.show({ title: 'Deleted', message: 'Provider deleted', color: 'green' });
      router.push('/admin/inference-providers');
    } catch (err: unknown) {
      notifications.show({
        title: 'Error',
        message: err instanceof Error ? err.message : 'Failed to delete provider',
        color: 'red',
      });
    } finally {
      setDeleteLoading(false);
    }
  };

  const handleTest = async () => {
    setTestLoading(true);
    try {
      const result = await testProvider(id);
      mutate();
      if (result.ok) {
        notifications.show({ title: 'Connection OK', message: `Responded in ${result.latencyMs}ms`, color: 'green' });
      } else {
        notifications.show({ title: 'Connection failed', message: result.error || 'The provider did not respond successfully.', color: 'red' });
      }
    } catch (err: unknown) {
      notifications.show({ title: 'Error', message: err instanceof Error ? err.message : 'Test failed', color: 'red' });
    } finally {
      setTestLoading(false);
    }
  };

  return (
    <>
      <PageHeader
        title={provider.name}
        breadcrumbs={[{ label: 'Inference Providers', href: '/admin/inference-providers' }, { label: provider.name }]}
      >
        <Button variant="default" leftSection={<IconPlugConnected size={16} />} onClick={handleTest} loading={testLoading}>
          Test connection
        </Button>
        <Button variant="default" leftSection={<IconEdit size={16} />} onClick={() => setEditOpened(true)}>
          Edit
        </Button>
        <Button color="red" variant="outline" leftSection={<IconTrash size={16} />} onClick={() => setDeleteOpened(true)}>
          Delete
        </Button>
      </PageHeader>

      <Card withBorder>
        <Stack gap="sm">
          <Group>
            <Text size="sm" fw={500} w={{ base: '100%', sm: 140 }}>Kind</Text>
            <Badge color={provider.kind === 'AzureOpenAI' ? 'blue' : 'grape'} variant="light">
              {provider.kind === 'AzureOpenAI' ? 'Azure OpenAI' : 'OpenAI-compatible'}
            </Badge>
          </Group>
          <Group>
            <Text size="sm" fw={500} w={{ base: '100%', sm: 140 }}>Capability</Text>
            <Badge color={provider.capability === 'Transcription' ? 'teal' : 'indigo'} variant="light">
              {provider.capability === 'Transcription' ? 'Transcription' : 'Chat'}
            </Badge>
          </Group>
          <Group>
            <Text size="sm" fw={500} w={{ base: '100%', sm: 140 }}>{provider.kind === 'AzureOpenAI' ? 'Endpoint' : 'Base URL'}</Text>
            <Text size="sm" style={{ wordBreak: 'break-all' }}>{provider.endpoint}</Text>
          </Group>
          <Group>
            <Text size="sm" fw={500} w={{ base: '100%', sm: 140 }}>{provider.kind === 'AzureOpenAI' ? 'Deployment name' : 'Model'}</Text>
            <Text size="sm">{provider.modelName}</Text>
          </Group>
          <Group>
            <Text size="sm" fw={500} w={{ base: '100%', sm: 140 }}>API Key</Text>
            <Text size="sm">
              {provider.hasApiKey ? `•••• ${provider.apiKeyLastFour ?? ''}` : 'Not set'}
            </Text>
          </Group>
          <Group>
            <Text size="sm" fw={500} w={{ base: '100%', sm: 140 }}>Status</Text>
            <Group gap="xs">
              {provider.isDefault && <Badge color="violet" variant="light">Default</Badge>}
              <Badge color={provider.isEnabled ? 'green' : 'gray'} variant="light">
                {provider.isEnabled ? 'Enabled' : 'Disabled'}
              </Badge>
            </Group>
          </Group>
          <Group>
            <Text size="sm" fw={500} w={{ base: '100%', sm: 140 }}>Timeout</Text>
            <Text size="sm">{provider.timeoutSeconds ? `${provider.timeoutSeconds}s` : 'Default'}</Text>
          </Group>
          <Group>
            <Text size="sm" fw={500} w={{ base: '100%', sm: 140 }}>Last Test</Text>
            {provider.lastTestAt ? (
              <Group gap="xs">
                <Badge color={provider.lastTestOk ? 'green' : 'red'} variant="light">
                  {provider.lastTestOk ? 'Succeeded' : 'Failed'}
                </Badge>
                <Text size="sm" c="dimmed">{formatDate(provider.lastTestAt)}</Text>
              </Group>
            ) : (
              <Text size="sm" c="dimmed">Never tested</Text>
            )}
          </Group>
          {provider.lastTestError && (
            <Group align="flex-start">
              <Text size="sm" fw={500} w={{ base: '100%', sm: 140 }}>Last Error</Text>
              <Text size="sm" c="red">{provider.lastTestError}</Text>
            </Group>
          )}
          <Group>
            <Text size="sm" fw={500} w={{ base: '100%', sm: 140 }}>Created</Text>
            <Text size="sm">{formatDate(provider.createdAt)}</Text>
          </Group>
          <Group>
            <Text size="sm" fw={500} w={{ base: '100%', sm: 140 }}>Updated</Text>
            <Text size="sm">{formatDate(provider.updatedAt)}</Text>
          </Group>
        </Stack>
      </Card>

      <InferenceProviderForm
        opened={editOpened}
        onClose={() => setEditOpened(false)}
        onSuccess={() => mutate()}
        provider={provider}
      />
      <ConfirmModal
        opened={deleteOpened}
        onClose={() => setDeleteOpened(false)}
        onConfirm={handleDelete}
        title="Delete Provider"
        message={`Are you sure you want to delete ${provider.name}? This action cannot be undone.`}
        loading={deleteLoading}
      />
    </>
  );
}
