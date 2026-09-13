'use client';

import { Card, Text, Stack, Group, Badge, Loader } from '@mantine/core';
import { IconServer, IconPlugConnected } from '@tabler/icons-react';
import Link from 'next/link';
import { PageHeader } from '@/components/shared/PageHeader';
import { useWorkflowEngineStatus } from '@/lib/hooks/use-workflow-engine';
import { useInferenceProviders } from '@/lib/hooks/use-inference-providers';
import { formatDate } from '@/lib/utils/format';

export default function AdminOverviewPage() {
  const { data: engineStatus, isLoading, error } = useWorkflowEngineStatus();
  const { data: providers, isLoading: providersLoading, error: providersError } = useInferenceProviders();
  const defaultProvider = providers?.find((p) => p.isDefault && p.capability === 'Chat');

  return (
    <>
      <PageHeader
        title="Admin Overview"
        breadcrumbs={[{ label: 'Admin' }, { label: 'Overview' }]}
      />
      <Stack gap="md" maw={600}>
        <Card
          withBorder
          padding="md"
          radius="md"
          component={Link}
          href="/admin/workflow-service"
          style={{ cursor: 'pointer' }}
        >
          <Group justify="space-between" mb="sm">
            <Group gap="sm">
              <IconServer size={20} />
              <Text fw={600}>Workflow Engine</Text>
            </Group>
            {isLoading ? (
              <Loader size="xs" />
            ) : error ? (
              <Badge color="red" variant="filled">Unreachable</Badge>
            ) : (
              <Badge color="green" variant="filled">Healthy</Badge>
            )}
          </Group>
          {engineStatus && (
            <Stack gap={4}>
              <Text size="sm" c="dimmed">Service: {engineStatus.service}</Text>
              <Text size="sm" c="dimmed">Status: {engineStatus.status}</Text>
              <Text size="sm" c="dimmed">Last checked: {formatDate(engineStatus.timestamp)}</Text>
            </Stack>
          )}
          {error && !isLoading && (
            <Text size="sm" c="red">Could not connect to the Workflow Engine service.</Text>
          )}
          <Text size="sm" mt="sm" c="cyan.4">Open live command center</Text>
        </Card>

        <Card
          withBorder
          padding="md"
          radius="md"
          component={Link}
          href="/admin/inference-providers"
          style={{ cursor: 'pointer' }}
        >
          <Group justify="space-between" mb="sm">
            <Group gap="sm">
              <IconPlugConnected size={20} />
              <Text fw={600}>Default Inference Provider</Text>
            </Group>
            {providersLoading ? (
              <Loader size="xs" />
            ) : providersError ? (
              <Badge color="red" variant="filled">Unreachable</Badge>
            ) : !defaultProvider ? (
              <Badge color="orange" variant="filled">Not configured</Badge>
            ) : defaultProvider.lastTestOk === false ? (
              <Badge color="red" variant="filled">Last test failed</Badge>
            ) : (
              <Badge color="green" variant="filled">Configured</Badge>
            )}
          </Group>
          {defaultProvider && (
            <Stack gap={4}>
              <Text size="sm" c="dimmed">Name: {defaultProvider.name}</Text>
              <Text size="sm" c="dimmed">
                Kind: {defaultProvider.kind === 'AzureOpenAI' ? 'Azure OpenAI' : 'OpenAI-compatible'}
              </Text>
              <Text size="sm" c="dimmed">
                Last test: {defaultProvider.lastTestAt
                  ? `${defaultProvider.lastTestOk ? 'OK' : 'Failed'} — ${formatDate(defaultProvider.lastTestAt)}`
                  : 'Never tested'}
              </Text>
            </Stack>
          )}
          {providersError && !providersLoading && (
            <Text size="sm" c="red">Could not connect to the Inference API.</Text>
          )}
          {!providersLoading && !providersError && !defaultProvider && (
            <Text size="sm" c="dimmed">
              No default provider set — agents fall back to the AzureOpenAI:* config keys.
            </Text>
          )}
          <Text size="sm" mt="sm" c="cyan.4">Manage inference providers</Text>
        </Card>
      </Stack>
    </>
  );
}
