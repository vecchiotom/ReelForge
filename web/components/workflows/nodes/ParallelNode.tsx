'use client';

import { memo, useState } from 'react';
import { Handle, Position } from '@xyflow/react';
import {
  Card, Group, Text, ActionIcon, Badge, Stack, TextInput,
  Table, ThemeIcon, Tooltip,
} from '@mantine/core';
import { IconTrash, IconGitFork, IconX, IconRobot } from '@tabler/icons-react';
import { motion } from 'framer-motion';
import { AgentPicker } from '../AgentPicker';
import { useAgents } from '@/lib/hooks/use-agents';
import type { StepData } from '../WorkflowStepList';

interface ParallelNodeData {
  step: StepData;
  stepNumber: number;
  onChange: (updates: Partial<StepData>) => void;
  onRemove: () => void;
}

export const ParallelNode = memo(({ data }: { data: ParallelNodeData }) => {
  const { step, stepNumber, onChange, onRemove } = data;
  const [expanded, setExpanded] = useState(false);
  const [pickerValue, setPickerValue] = useState<string | null>(null);
  const { data: agents } = useAgents();

  const agentIds = step.parallelAgentIds ?? [];

  const getAgentName = (id: string) => agents?.find((a) => a.id === id)?.name ?? id;

  const addAgent = (agentId: string) => {
    if (!agentId || agentIds.includes(agentId)) return;
    const updated = [...agentIds, agentId];
    onChange({ parallelAgentIds: updated, agentDefinitionId: updated[0] ?? '' });
    setPickerValue(null);
  };

  const removeAgent = (index: number) => {
    const updated = agentIds.filter((_, i) => i !== index);
    onChange({ parallelAgentIds: updated, agentDefinitionId: updated[0] ?? '' });
  };

  return (
    <>
      <Handle type="target" position={Position.Top} style={{ background: '#0ea5e9' }} />

      <motion.div
        initial={{ scale: 0.8, opacity: 0 }}
        animate={{ scale: 1, opacity: 1 }}
        transition={{ duration: 0.3 }}
      >
        <Card
          shadow="md"
          padding="md"
          radius="lg"
          style={{
            width: 360,
            border: '2px solid #0ea5e9',
            background: 'linear-gradient(135deg, #ffffff 0%, #f0f9ff 100%)',
            cursor: 'pointer',
          }}
          onMouseEnter={() => setExpanded(true)}
          onMouseLeave={() => setExpanded(false)}
        >
          <Stack gap="sm">
            {/* Header */}
            <Group justify="space-between" wrap="nowrap">
              <Group gap="xs">
                <div
                  style={{
                    width: 32,
                    height: 32,
                    borderRadius: '50%',
                    background: 'linear-gradient(135deg, #0ea5e9 0%, #0284c7 100%)',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    color: 'white',
                    fontWeight: 700,
                    fontSize: 14,
                  }}
                >
                  {stepNumber}
                </div>
                <Badge
                  size="lg"
                  variant="gradient"
                  gradient={{ from: 'cyan', to: 'blue', deg: 90 }}
                  leftSection={<IconGitFork size={14} />}
                >
                  PARALLEL
                </Badge>
                <Badge size="sm" variant="outline" color="cyan">
                  {agentIds.length} agent{agentIds.length !== 1 ? 's' : ''}
                </Badge>
              </Group>
              <Tooltip label="Delete Step">
                <ActionIcon
                  color="red"
                  variant="subtle"
                  onClick={(e) => {
                    e.stopPropagation();
                    onRemove();
                  }}
                >
                  <IconTrash size={16} />
                </ActionIcon>
              </Tooltip>
            </Group>

            {/* Label */}
            <TextInput
              placeholder="Step label (e.g. Parallel Analysis)"
              value={step.label}
              onChange={(e) => onChange({ label: e.target.value })}
              size="sm"
              styles={{ input: { fontWeight: 600, fontSize: 15, border: 'none', background: 'transparent', padding: 0 } }}
              onClick={(e) => e.stopPropagation()}
            />

            {/* Expanded: agent list */}
            {expanded && (
              <Stack gap="xs" onClick={(e) => e.stopPropagation()}>
                <Text size="xs" c="dimmed" fw={600}>
                  Agents run in parallel — their outputs are merged into a JSON array for the next step.
                </Text>

                {agentIds.length > 0 && (
                  <Table striped highlightOnHover withTableBorder withColumnBorders fz="xs">
                    <Table.Thead>
                      <Table.Tr>
                        <Table.Th>#</Table.Th>
                        <Table.Th>Agent</Table.Th>
                        <Table.Th style={{ width: 32 }} />
                      </Table.Tr>
                    </Table.Thead>
                    <Table.Tbody>
                      {agentIds.map((id, i) => (
                        <Table.Tr key={id}>
                          <Table.Td>
                            <ThemeIcon size="sm" variant="light" color="cyan" radius="xl">
                              <IconRobot size={11} />
                            </ThemeIcon>
                          </Table.Td>
                          <Table.Td>
                            <Text size="xs" truncate maw={220}>{getAgentName(id)}</Text>
                          </Table.Td>
                          <Table.Td>
                            <ActionIcon
                              size="xs"
                              color="red"
                              variant="subtle"
                              onClick={() => removeAgent(i)}
                            >
                              <IconX size={12} />
                            </ActionIcon>
                          </Table.Td>
                        </Table.Tr>
                      ))}
                    </Table.Tbody>
                  </Table>
                )}

                <Group gap="xs" align="flex-end">
                  <div style={{ flex: 1 }}>
                    <AgentPicker
                      value={pickerValue}
                      onChange={(id) => {
                        if (id) addAgent(id);
                      }}
                      placeholder="Add agent to parallel group…"
                      excludeIds={agentIds}
                    />
                  </div>
                </Group>

                <Text size="xs" c="dimmed">
                  Output format: <code>[{`{"agentName":"...","output":"..."}`}, ...]</code>
                </Text>
              </Stack>
            )}
          </Stack>
        </Card>
      </motion.div>

      <Handle type="source" position={Position.Bottom} style={{ background: '#0ea5e9' }} />
    </>
  );
});

ParallelNode.displayName = 'ParallelNode';
