'use client';

import { useEffect, useState } from 'react';
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
  { value: 'Anthropic', label: 'Anthropic (Claude)' },
];

const CAPABILITY_OPTIONS: { value: InferenceProviderCapability; label: string }[] = [
  { value: 'Chat', label: 'Chat' },
  { value: 'Transcription', label: 'Transcription (ASR)' },
  { value: 'Vision', label: 'Vision (shot captioning)' },
];

/** Anthropic's production API. Prefilled because the backend requires a non-empty endpoint. */
const ANTHROPIC_DEFAULT_ENDPOINT = 'https://api.anthropic.com';

/**
 * Per-kind field wording and examples. Keyed by kind rather than branched on `isAzure`, so a third
 * kind does not turn every label into a nested ternary.
 */
const KIND_FIELD_HINTS: Record<
  InferenceProviderKind,
  { endpointLabel: string; modelLabel: string; endpointPlaceholder: string; modelPlaceholder: string }
> = {
  AzureOpenAI: {
    endpointLabel: 'Endpoint',
    modelLabel: 'Deployment name',
    endpointPlaceholder: 'https://my-resource.openai.azure.com',
    modelPlaceholder: 'gpt-4o-mini',
  },
  OpenAICompatible: {
    endpointLabel: 'Base URL',
    modelLabel: 'Model',
    endpointPlaceholder: 'http://localhost:8000/v1',
    modelPlaceholder: 'meta-llama/Llama-3-8b',
  },
  Anthropic: {
    endpointLabel: 'Base URL',
    modelLabel: 'Model',
    endpointPlaceholder: ANTHROPIC_DEFAULT_ENDPOINT,
    modelPlaceholder: 'claude-opus-5',
  },
};

/** Anthropic has no speech-to-text API; the backend rejects this pairing with a 400. */
const CAPABILITIES_UNSUPPORTED_BY_KIND: Partial<Record<InferenceProviderKind, InferenceProviderCapability[]>> = {
  Anthropic: ['Transcription'],
};

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

  // `form`'s own `initialValues` are captured once at mount and never track later prop changes,
  // so `form.reset()` after a save would restore whatever `provider` looked like when this modal
  // instance first mounted — not the value just saved (the page that owns this form keeps a single
  // instance alive across edits, found by Copilot review). Re-seed from the current `provider` every
  // time the modal opens instead, so a reopen always shows the latest saved data.
  useEffect(() => {
    if (opened) {
      form.setValues({
        name: provider?.name || '',
        kind: (provider?.kind || 'AzureOpenAI') as InferenceProviderKind,
        capability: (provider?.capability || 'Chat') as InferenceProviderCapability,
        endpoint: provider?.endpoint || '',
        modelName: provider?.modelName || '',
        apiKey: '',
        isDefault: provider?.isDefault || false,
        isEnabled: provider?.isEnabled ?? true,
        timeoutSeconds: provider?.timeoutSeconds ?? undefined,
      });
      form.resetDirty();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [opened, provider]);

  const { endpointLabel, modelLabel, endpointPlaceholder, modelPlaceholder } =
    KIND_FIELD_HINTS[form.values.kind] ?? KIND_FIELD_HINTS.OpenAICompatible;

  const unsupportedCapabilities = CAPABILITIES_UNSUPPORTED_BY_KIND[form.values.kind] ?? [];
  const capabilityOptions = CAPABILITY_OPTIONS.filter((o) => !unsupportedCapabilities.includes(o.value));

  // Switching kind can invalidate the currently selected capability (Anthropic cannot transcribe).
  // Leaving the stale value selected would submit a combination the backend rejects with a 400 and
  // — worse — the Select would render a value no longer in its option list, showing blank. Fall
  // back to Chat, which every kind supports. Endpoint is prefilled on the same switch because the
  // backend requires a non-empty one and Anthropic's is a fixed, well-known URL.
  const handleKindChange = (value: string | null) => {
    if (!value) return;
    const kind = value as InferenceProviderKind;
    form.setFieldValue('kind', kind);

    if ((CAPABILITIES_UNSUPPORTED_BY_KIND[kind] ?? []).includes(form.values.capability)) {
      form.setFieldValue('capability', 'Chat');
    }

    if (kind === 'Anthropic' && !form.values.endpoint.trim()) {
      form.setFieldValue('endpoint', ANTHROPIC_DEFAULT_ENDPOINT);
    }
  };

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
            onChange={handleKindChange}
          />
          <Select
            label="Capability"
            description="Chat providers serve agent completions; Transcription providers serve ASR for VideoAnalyze steps; Vision providers serve VideoAnalyze's optional shot captioning. Each has its own independent default."
            data={capabilityOptions}
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
            label={`Set as default ${
              form.values.capability === 'Transcription'
                ? 'transcription'
                : form.values.capability === 'Vision'
                  ? 'vision'
                  : 'chat'
            } provider`}
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
