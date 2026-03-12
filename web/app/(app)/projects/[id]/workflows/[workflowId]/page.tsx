'use client';

import { use, useState, useEffect } from 'react';
import { TextInput, Button, Stack, Group, Loader, Center, Text, Divider, Switch } from '@mantine/core';
import { IconPlayerPlay, IconTrash } from '@tabler/icons-react';
import { useForm } from '@mantine/form';
import { notifications } from '@mantine/notifications';
import { useWorkflow } from '@/lib/hooks/use-workflows';
import { updateWorkflow, deleteWorkflow, executeWorkflow } from '@/lib/api/workflows';
import { PageHeader } from '@/components/shared/PageHeader';
import { FlowchartBuilderWrapper } from '@/components/workflows/FlowchartBuilder';
import { ExecutionHistory } from '@/components/workflows/ExecutionHistory';
import { ConfirmModal } from '@/components/shared/ConfirmModal';
import { ExecuteWithInputModal } from '@/components/workflows/ExecuteWithInputModal';
import type { StepData } from '@/components/workflows/WorkflowStepList';
import { useRouter } from 'next/navigation';

export default function WorkflowEditPage({ params }: { params: Promise<{ id: string; workflowId: string }> }) {
  const { id: projectId, workflowId } = use(params);
  const { data: workflow, isLoading, mutate } = useWorkflow(projectId, workflowId);
  const router = useRouter();
  const [saving, setSaving] = useState(false);
  const [executing, setExecuting] = useState(false);
  const [executeModalOpened, setExecuteModalOpened] = useState(false);
  const [deleteOpened, setDeleteOpened] = useState(false);
  const [deleteLoading, setDeleteLoading] = useState(false);
  const [steps, setSteps] = useState<StepData[] | null>(null);
  const [initialized, setInitialized] = useState(false);

  const form = useForm({
    initialValues: { name: '', requiresUserInput: false },
    validate: {
      name: (v) => (!v.trim() ? 'Name is required' : null),
    },
  });

  useEffect(() => {
    if (workflow && !initialized) {
      form.setValues({ name: workflow.name, requiresUserInput: workflow.requiresUserInput ?? false });
      setSteps(
        workflow.steps
          .sort((a, b) => a.stepOrder - b.stepOrder)
          .map((s) => ({
            id: s.id,
            label: s.label,
            agentDefinitionId: s.agentDefinitionId,
            stepType: s.stepType || 'Agent',
            conditionExpression: s.conditionExpression ?? null,
            loopSourceExpression: s.loopSourceExpression ?? null,
            loopTargetStepOrder: s.loopTargetStepOrder ?? null,
            maxIterations: s.maxIterations ?? 3,
            minScore: s.minScore ?? null,
            inputMappingJson: s.inputMappingJson ?? null,
            agentInputContextMode: s.agentInputContextMode ?? null,
            selectedPriorStepOrders: s.selectedPriorStepOrdersJson ? JSON.parse(s.selectedPriorStepOrdersJson) : [],
            trueBranchStepOrder: s.trueBranchStepOrder ?? null,
            falseBranchStepOrder: s.falseBranchStepOrder ?? null,
            parallelAgentIds: s.parallelAgentIdsJson ? JSON.parse(s.parallelAgentIdsJson) : [],
          })),
      );
      setInitialized(true);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [workflow, initialized]);

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
        name: values.name,
        requiresUserInput: values.requiresUserInput,
        steps: steps.map((s, i) => ({
          agentDefinitionId: s.agentDefinitionId,
          stepOrder: i + 1,
          label: s.label || `Step ${i + 1}`,
          stepType: s.stepType,
          conditionExpression: s.conditionExpression,
          loopSourceExpression: s.loopSourceExpression,
          loopTargetStepOrder: s.loopTargetStepOrder,
          maxIterations: s.maxIterations,
          minScore: s.minScore,
          inputMappingJson: s.inputMappingJson,
          agentInputContextMode: s.agentInputContextMode,
          selectedPriorStepOrdersJson: s.selectedPriorStepOrders.length > 0 ? JSON.stringify(s.selectedPriorStepOrders) : null,
          trueBranchStepOrder: s.trueBranchStepOrder,
          falseBranchStepOrder: s.falseBranchStepOrder,
          parallelAgentIdsJson: s.parallelAgentIds.length > 0 ? JSON.stringify(s.parallelAgentIds) : null,
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

  const handleExecuteClick = () => {
    if (workflow.requiresUserInput) {
      setExecuteModalOpened(true);
    } else {
      void doExecute(null);
    }
  };

  const doExecute = async (userRequest: string | null) => {
    setExecuting(true);
    try {
      const execution = await executeWorkflow(projectId, workflowId, userRequest);
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
        <Button
          leftSection={<IconPlayerPlay size={16} />}
          onClick={handleExecuteClick}
          loading={executing}
          variant="gradient"
          gradient={{ from: 'violet', to: 'purple' }}
        >
          Execute
        </Button>
        <Button color="red" variant="outline" leftSection={<IconTrash size={16} />} onClick={() => setDeleteOpened(true)}>
          Delete
        </Button>
      </PageHeader>

      <form onSubmit={handleSave}>
        <Stack gap="lg" maw={1200}>
          <TextInput label="Workflow Name" size="lg" {...form.getInputProps('name')} />
          <Switch
            label="Require user input on execution"
            description="When enabled, users are prompted to provide a free-text request before the workflow runs. The request is passed as context to all agents."
            checked={form.values.requiresUserInput}
            onChange={(e) => form.setFieldValue('requiresUserInput', e.currentTarget.checked)}
          />
          {steps && <FlowchartBuilderWrapper steps={steps} onChange={setSteps} />}
          <Group>
            <Button type="submit" loading={saving} size="lg" variant="gradient" gradient={{ from: 'violet', to: 'purple' }}>
              Save Changes
            </Button>
          </Group>
        </Stack>
      </form>

      <Divider my="xl" />
      <ExecutionHistory projectId={projectId} workflowId={workflowId} />

      <ExecuteWithInputModal
        opened={executeModalOpened}
        onClose={() => setExecuteModalOpened(false)}
        onConfirm={(userRequest) => {
          setExecuteModalOpened(false);
          void doExecute(userRequest);
        }}
        executing={executing}
      />

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
