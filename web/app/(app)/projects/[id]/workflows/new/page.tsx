'use client';

import { use, useState } from 'react';
import { TextInput, Button, Stack } from '@mantine/core';
import { useForm } from '@mantine/form';
import { notifications } from '@mantine/notifications';
import { createWorkflow } from '@/lib/api/workflows';
import { PageHeader } from '@/components/shared/PageHeader';
import { FlowchartBuilderWrapper } from '@/components/workflows/FlowchartBuilder';
import type { StepData } from '@/components/workflows/WorkflowStepList';
import { useRouter } from 'next/navigation';

export default function NewWorkflowPage({ params }: { params: Promise<{ id: string }> }) {
  const { id: projectId } = use(params);
  const router = useRouter();
  const [loading, setLoading] = useState(false);
  const [steps, setSteps] = useState<StepData[]>([]);

  const form = useForm({
    initialValues: { name: '' },
    validate: {
      name: (v) => (!v.trim() ? 'Name is required' : null),
    },
  });

  const handleSubmit = form.onSubmit(async (values) => {
    if (steps.length === 0) {
      notifications.show({ title: 'Error', message: 'Add at least one step', color: 'red' });
      return;
    }

    for (const [i, s] of steps.entries()) {
      if (s.stepType !== 'Conditional' && s.stepType !== 'Parallel' && !s.agentDefinitionId) {
        notifications.show({ title: 'Error', message: `Step ${i + 1} requires an agent`, color: 'red' });
        return;
      }
      if (s.stepType === 'Parallel' && s.parallelAgentIds.length === 0) {
        notifications.show({ title: 'Error', message: `Step ${i + 1} requires at least one agent`, color: 'red' });
        return;
      }
      if (s.stepType === 'Conditional' && !s.conditionExpression) {
        notifications.show({ title: 'Error', message: `Step ${i + 1} requires a condition expression`, color: 'red' });
        return;
      }
    }

    setLoading(true);
    try {
      const workflow = await createWorkflow(projectId, {
        name: values.name,
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
      notifications.show({ title: 'Created', message: 'Workflow created', color: 'green' });
      router.push(`/projects/${projectId}/workflows/${workflow.id}`);
    } catch (err: unknown) {
      notifications.show({
        title: 'Error',
        message: err instanceof Error ? err.message : 'Failed to create workflow',
        color: 'red',
      });
    } finally {
      setLoading(false);
    }
  });

  return (
    <>
      <PageHeader
        title="New Workflow"
        breadcrumbs={[
          { label: 'Projects', href: '/projects' },
          { label: 'Project', href: `/projects/${projectId}` },
          { label: 'New Workflow' },
        ]}
      />
      <form onSubmit={handleSubmit}>
        <Stack gap="lg" maw={1200}>
          <TextInput
            label="Workflow Name"
            placeholder="My Workflow"
            size="lg"
            {...form.getInputProps('name')}
          />
          <FlowchartBuilderWrapper steps={steps} onChange={setSteps} />
          <Button type="submit" loading={loading} size="lg" variant="gradient" gradient={{ from: 'violet', to: 'purple' }}>
            Create Workflow
          </Button>
        </Stack>
      </form>
    </>
  );
}
