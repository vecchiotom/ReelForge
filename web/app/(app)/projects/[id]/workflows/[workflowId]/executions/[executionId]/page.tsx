'use client';

import { use } from 'react';
import { Loader, Center, Text } from '@mantine/core';
import { useExecution } from '@/lib/hooks/use-execution';
import { useWorkflow } from '@/lib/hooks/use-workflows';
import { PageHeader } from '@/components/shared/PageHeader';
import { ExecutionProgress } from '@/components/workflows/ExecutionProgress';

export default function ExecutionDetailPage({
  params,
}: {
  params: Promise<{ id: string; workflowId: string; executionId: string }>;
}) {
  const { id: projectId, workflowId, executionId } = use(params);
  const { data: execution, isLoading } = useExecution(projectId, workflowId, executionId);
  const { data: workflow } = useWorkflow(projectId, workflowId);

  if (isLoading) return <Center h={300}><Loader /></Center>;
  if (!execution) return <Text>Execution not found</Text>;

  return (
    <>
      <PageHeader
        title={`Execution ${executionId.slice(0, 8)}...`}
        breadcrumbs={[
          { label: 'Projects', href: '/projects' },
          { label: 'Project', href: `/projects/${projectId}` },
          { label: workflow?.name || 'Workflow', href: `/projects/${projectId}/workflows/${workflowId}` },
          { label: 'Execution' },
        ]}
      />
      <ExecutionProgress execution={execution} workflow={workflow} />
    </>
  );
}
