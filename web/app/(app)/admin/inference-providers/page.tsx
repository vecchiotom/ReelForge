'use client';

import { useState } from 'react';
import { Button, Loader, Center } from '@mantine/core';
import { IconPlus } from '@tabler/icons-react';
import { notifications } from '@mantine/notifications';
import { useInferenceProviders } from '@/lib/hooks/use-inference-providers';
import { deleteProvider } from '@/lib/api/inference-providers';
import { PageHeader } from '@/components/shared/PageHeader';
import { InferenceProviderTable } from '@/components/admin/InferenceProviderTable';
import { InferenceProviderForm } from '@/components/admin/InferenceProviderForm';
import { ConfirmModal } from '@/components/shared/ConfirmModal';
import { EmptyState } from '@/components/shared/EmptyState';
import type { InferenceProvider } from '@/lib/types/inference-provider';

export default function InferenceProvidersPage() {
  const { data: providers, isLoading, mutate } = useInferenceProviders();
  const [formOpened, setFormOpened] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<InferenceProvider | null>(null);
  const [deleteLoading, setDeleteLoading] = useState(false);

  const handleCreateSuccess = () => {
    mutate();
  };

  const handleDelete = async () => {
    if (!deleteTarget) return;
    setDeleteLoading(true);
    try {
      await deleteProvider(deleteTarget.id);
      mutate();
      notifications.show({ title: 'Deleted', message: 'Provider deleted', color: 'green' });
    } catch (err: unknown) {
      notifications.show({
        title: 'Error',
        message: err instanceof Error ? err.message : 'Failed to delete provider',
        color: 'red',
      });
    } finally {
      setDeleteLoading(false);
      setDeleteTarget(null);
    }
  };

  if (isLoading) {
    return <Center h={300}><Loader /></Center>;
  }

  return (
    <>
      <PageHeader title="Inference Providers">
        <Button leftSection={<IconPlus size={16} />} onClick={() => setFormOpened(true)}>
          New Provider
        </Button>
      </PageHeader>

      {providers && providers.length > 0 ? (
        <InferenceProviderTable providers={providers} onDelete={setDeleteTarget} />
      ) : (
        <EmptyState
          title="No inference providers"
          description="Create a provider (Azure OpenAI or an OpenAI-compatible endpoint) to configure how agents call models."
        />
      )}

      <InferenceProviderForm opened={formOpened} onClose={() => setFormOpened(false)} onSuccess={handleCreateSuccess} />

      <ConfirmModal
        opened={!!deleteTarget}
        onClose={() => setDeleteTarget(null)}
        onConfirm={handleDelete}
        title="Delete Provider"
        message={`Are you sure you want to delete ${deleteTarget?.name}? Agents overridden to use it will fall back to the default provider.`}
        loading={deleteLoading}
      />
    </>
  );
}
