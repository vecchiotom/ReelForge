'use client';

import { use, useState } from 'react';
import { TextInput, Textarea, Button, Stack, Group, Loader, Center, Text } from '@mantine/core';
import { IconPlayerPlay, IconTrash } from '@tabler/icons-react';
import { useForm } from '@mantine/form';
import { notifications } from '@mantine/notifications';
import { useWorkflow } from '@/lib/hooks/use-workflows';
import { updateWorkflow, deleteWorkflow, executeWorkflow } from '@/lib/api/workflows';
import { PageHeader } from '@/components/shared/PageHeader';
import { WorkflowStepList } from '@/components/workflows/WorkflowStepList';
import { ConfirmModal } from '@/components/shared/ConfirmModal';
import type { StepData } from '@/components/workflows/WorkflowStepList';
import { useRouter } from 'next/navigation';

export default function WorkflowEditPage({ params }: { params: Promise<{ id: string; workflowId: string }> }) {
  const { id: projectId, workflowId } = use(params);
  const { data: workflow, isLoading, mutate } = useWorkflow(projectId, workflowId);
  const router = useRouter();
  const [saving, setSaving] = useState(false);
  const [executing, setExecuting] = useState(false);
  const [deleteOpened, setDeleteOpened] = useState(false);
  const [deleteLoading, setDeleteLoading] = useState(false);
  const [steps, setSteps] = useState<StepData[] | null>(null);

  const form = useForm({
    initialValues: { name: '', description: '' },
    validate: {
      name: (v) => (!v.trim() ? 'Name is required' : null),
    },
  });

  // Initialize form when workflow loads
  if (workflow && !form.isDirty()) {
    form.setValues({ name: workflow.name, description: workflow.description });
    if (steps === null) {
      setSteps(
        workflow.steps
          .sort((a, b) => a.stepOrder - b.stepOrder)
          .map((s) => ({ id: s.id, label: s.label, agentDefinitionId: s.agentDefinitionId })),
      );
    }
  }

  if (isLoading) return <Center h={300}><Loader /></Center>;
  if (!workflow) return <Text>Workflow not found</Text>;

  const handleSave = form.onSubmit(async (values) => {
    if (!steps || steps.length === 0) {
      notifications.show({ title: 'Error', message: 'Add at least one step', color: 'red' });
      return;
    }
    setSaving(true);
    try {
      await updateWorkflow(projectId, workflowId, {
        ...values,
        steps: steps.map((s, i) => ({
          agentDefinitionId: s.agentDefinitionId,
          stepOrder: i + 1,
          label: s.label || `Step ${i + 1}`,
        })),
      });
      mutate();
      notifications.show({ title: 'Saved', message: 'Workflow updated', color: 'green' });
    } catch (err: unknown) {
      notifications.show({
        title: 'Error',
        message: err instanceof Error ? err.message : 'Failed to save',
        color: 'red',
      });
    } finally {
      setSaving(false);
    }
  });

  const handleExecute = async () => {
    setExecuting(true);
    try {
      const execution = await executeWorkflow(projectId, workflowId);
      notifications.show({ title: 'Started', message: 'Workflow execution started', color: 'blue' });
      router.push(`/projects/${projectId}/workflows/${workflowId}/executions/${execution.id}`);
    } catch (err: unknown) {
      notifications.show({
        title: 'Error',
        message: err instanceof Error ? err.message : 'Failed to execute',
        color: 'red',
      });
      setExecuting(false);
    }
  };

  const handleDelete = async () => {
    setDeleteLoading(true);
    try {
      await deleteWorkflow(projectId, workflowId);
      notifications.show({ title: 'Deleted', message: 'Workflow deleted', color: 'green' });
      router.push(`/projects/${projectId}`);
    } catch {
      notifications.show({ title: 'Error', message: 'Failed to delete', color: 'red' });
    } finally {
      setDeleteLoading(false);
    }
  };

  return (
    <>
      <PageHeader
        title={workflow.name}
        breadcrumbs={[
          { label: 'Projects', href: '/projects' },
          { label: 'Project', href: `/projects/${projectId}` },
          { label: workflow.name },
        ]}
      >
        <Button leftSection={<IconPlayerPlay size={16} />} onClick={handleExecute} loading={executing}>
          Execute
        </Button>
        <Button color="red" variant="outline" leftSection={<IconTrash size={16} />} onClick={() => setDeleteOpened(true)}>
          Delete
        </Button>
      </PageHeader>

      <form onSubmit={handleSave}>
        <Stack gap="md" maw={800}>
          <TextInput label="Name" {...form.getInputProps('name')} />
          <Textarea label="Description" rows={2} {...form.getInputProps('description')} />
          {steps && <WorkflowStepList steps={steps} onChange={setSteps} />}
          <Group>
            <Button type="submit" loading={saving}>Save Changes</Button>
          </Group>
        </Stack>
      </form>

      <ConfirmModal
        opened={deleteOpened}
        onClose={() => setDeleteOpened(false)}
        onConfirm={handleDelete}
        title="Delete Workflow"
        message="This will permanently delete this workflow and all its executions."
        loading={deleteLoading}
      />
    </>
  );
}
