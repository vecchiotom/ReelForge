'use client';

import { useState, useMemo } from 'react';
import { SimpleGrid, Button, Loader, Center, Text, Stack } from '@mantine/core';
import { IconPlus } from '@tabler/icons-react';
import { useAgents } from '@/lib/hooks/use-agents';
import { PageHeader } from '@/components/shared/PageHeader';
import { AgentCard } from '@/components/agents/AgentCard';
import { AgentForm } from '@/components/agents/AgentForm';
import { EmptyState } from '@/components/shared/EmptyState';

const CATEGORY_ORDER = ['Analysis', 'Translation', 'Production', 'Quality', 'FileProcessing', 'Custom'];

export default function AgentsPage() {
  const { data: agents, isLoading, mutate } = useAgents();
  const [formOpened, setFormOpened] = useState(false);

  const grouped = useMemo(() => {
    if (!agents) return {};
    const groups: Record<string, typeof agents> = {};
    for (const agent of agents) {
      const cat = agent.category || 'Custom';
      if (!groups[cat]) groups[cat] = [];
      groups[cat].push(agent);
    }
    return groups;
  }, [agents]);

  if (isLoading) {
    return <Center h={300}><Loader /></Center>;
  }

  return (
    <>
      <PageHeader title="Agents">
        <Button leftSection={<IconPlus size={16} />} onClick={() => setFormOpened(true)}>
          New Agent
        </Button>
      </PageHeader>

      {agents && agents.length > 0 ? (
        <Stack gap="xl">
          {CATEGORY_ORDER.filter((cat) => grouped[cat]).map((cat) => (
            <div key={cat}>
              <Text fw={600} size="lg" mb="sm">{cat}</Text>
              <SimpleGrid cols={{ base: 1, sm: 2, lg: 3 }}>
                {grouped[cat].map((agent) => (
                  <AgentCard key={agent.id} agent={agent} />
                ))}
              </SimpleGrid>
            </div>
          ))}
        </Stack>
      ) : (
        <EmptyState title="No agents" description="Agents will appear here once the system is configured." />
      )}

      <AgentForm opened={formOpened} onClose={() => setFormOpened(false)} onSuccess={() => mutate()} />
    </>
  );
}
