'use client';

import { useState } from 'react';
import { Modal, TextInput, Select, Switch, NumberInput, Button, Stack, Group } from '@mantine/core';
import { useForm } from '@mantine/form';
import { notifications } from '@mantine/notifications';
import { IconPlugConnected } from '@tabler/icons-react';
import { createProvider, updateProvider, testProviderConfig } from '@/lib/api/inference-providers';
import type { InferenceProvider, InferenceProviderKind, InferenceProviderCapability } from '@/lib/types/inference-provider';

interface InferenceProviderFormProps {
  opened: boolean;
  onClose: () => void;
  onSuccess: () => void;
  provider?: InferenceProvider;
}

const KIND_OPTIONS: { value: InferenceProviderKind; label: string }[] = [
  { value: 'AzureOpenAI', label: 'Azure OpenAI' },
  { value: 'OpenAICompatible', label: 'OpenAI-compatible' },
];

const CAPABILITY_OPTIONS: { value: InferenceProviderCapability; label: string }[] = [
  { value: 'Chat', label: 'Chat' },
  { value: 'Transcription', label: 'Transcription (ASR)' },
];

export function InferenceProviderForm({ opened, onClose, onSuccess, provider }: InferenceProviderFormProps) {
  const [loading, setLoading] = useState(false);
  const [testing, setTesting] = useState(false);
  const isEdit = !!provider;

  const form = useForm({
    initialValues: {
      name: provider?.name || '',
      kind: (provider?.kind || 'AzureOpenAI') as InferenceProviderKind,
      capability: (provider?.capability || 'Chat') as InferenceProviderCapability,
      endpoint: provider?.endpoint || '',
      modelName: provider?.modelName || '',
      apiKey: '',
      isDefault: provider?.isDefault || false,
      isEnabled: provider?.isEnabled ?? true,
      timeoutSeconds: provider?.timeoutSeconds ?? undefined,
    },
    validate: {
      name: (v) => (!v.trim() ? 'Name is required' : null),
      endpoint: (v) => (!v.trim() ? 'This field is required' : null),
      modelName: (v) => (!v.trim() ? 'This field is required' : null),
    },
  });

  const isAzure = form.values.kind === 'AzureOpenAI';
  const endpointLabel = isAzure ? 'Endpoint' : 'Base URL';
  const modelLabel = isAzure ? 'Deployment name' : 'Model';
  const endpointPlaceholder = isAzure ? 'https://my-resource.openai.azure.com' : 'http://localhost:8000/v1';
  const modelPlaceholder = isAzure ? 'gpt-4o-mini' : 'meta-llama/Llama-3-8b';

  const handleTest = async () => {
    setTesting(true);
    try {
      const result = await testProviderConfig({
        id: provider?.id,
        kind: form.values.kind,
        capability: form.values.capability,
        endpoint: form.values.endpoint,
        modelName: form.values.modelName,
        apiKey: form.values.apiKey ? form.values.apiKey : undefined,
      });
      if (result.ok) {
        notifications.show({
          title: 'Connection OK',
          message: `Responded in ${result.latencyMs}ms`,
          color: 'green',
        });
      } else {
        notifications.show({
          title: 'Connection failed',
          message: result.error || 'The provider did not respond successfully.',
          color: 'red',
        });
      }
    } catch (err: unknown) {
      notifications.show({
        title: 'Error',
        message: err instanceof Error ? err.message : 'Test failed',
        color: 'red',
      });
    } finally {
      setTesting(false);
    }
  };

  const handleSubmit = form.onSubmit(async (values) => {
    setLoading(true);
    try {
      if (isEdit) {
        await updateProvider(provider.id, {
          name: values.name,
          kind: values.kind,
          capability: values.capability,
          endpoint: values.endpoint,
          modelName: values.modelName,
          apiKey: values.apiKey ? values.apiKey : undefined,
          isDefault: values.isDefault,
          isEnabled: values.isEnabled,
          timeoutSeconds: values.timeoutSeconds || undefined,
        });
        notifications.show({ title: 'Updated', message: 'Provider updated', color: 'green' });
      } else {
        await createProvider({
          name: values.name,
          kind: values.kind,
          capability: values.capability,
          endpoint: values.endpoint,
          modelName: values.modelName,
          apiKey: values.apiKey ? values.apiKey : undefined,
          isDefault: values.isDefault,
          isEnabled: values.isEnabled,
          timeoutSeconds: values.timeoutSeconds || undefined,
        });
        notifications.show({ title: 'Created', message: 'Provider created', color: 'green' });
      }
      onSuccess();
      form.reset();
      onClose();
    } catch (err: unknown) {
      notifications.show({
        title: 'Error',
        message: err instanceof Error ? err.message : 'Operation failed',
        color: 'red',
      });
    } finally {
      setLoading(false);
    }
  });

  return (
    <Modal opened={opened} onClose={onClose} title={isEdit ? 'Edit Provider' : 'New Provider'} centered>
      <form onSubmit={handleSubmit}>
        <Stack>
          <TextInput label="Name" placeholder="Production Azure" {...form.getInputProps('name')} />
          <Select
            label="Kind"
            data={KIND_OPTIONS}
            allowDeselect={false}
            {...form.getInputProps('kind')}
          />
          <Select
            label="Capability"
            description="Chat providers serve agent completions; Transcription providers serve ASR for VideoAnalyze steps. Each has its own independent default."
            data={CAPABILITY_OPTIONS}
            allowDeselect={false}
            {...form.getInputProps('capability')}
          />
          <TextInput label={endpointLabel} placeholder={endpointPlaceholder} {...form.getInputProps('endpoint')} />
          <TextInput label={modelLabel} placeholder={modelPlaceholder} {...form.getInputProps('modelName')} />
          <TextInput
            label="API Key"
            type="password"
            placeholder={provider?.apiKeyLastFour ? `•••• ${provider.apiKeyLastFour}` : 'sk-...'}
            description={isEdit ? 'Leave blank to keep the current key' : undefined}
            {...form.getInputProps('apiKey')}
          />
          <NumberInput
            label="Timeout (seconds)"
            placeholder="Default"
            min={1}
            {...form.getInputProps('timeoutSeconds')}
          />
          <Switch
            label={`Set as default ${form.values.capability === 'Transcription' ? 'transcription' : 'chat'} provider`}
            {...form.getInputProps('isDefault', { type: 'checkbox' })}
          />
          <Switch label="Enabled" {...form.getInputProps('isEnabled', { type: 'checkbox' })} />

          <Group justify="space-between" mt="sm">
            <Button
              variant="default"
              leftSection={<IconPlugConnected size={16} />}
              onClick={handleTest}
              loading={testing}
              disabled={!form.values.endpoint || !form.values.modelName}
            >
              Test connection
            </Button>
            <Button type="submit" loading={loading}>
              {isEdit ? 'Update' : 'Create'}
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}
