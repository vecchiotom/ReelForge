'use client';

import { memo, useState } from 'react';
import { Handle, Position } from '@xyflow/react';
import { Card, Group, Text, ActionIcon, Badge, Stack, TextInput, Collapse, Button, Tooltip, Select, MultiSelect } from '@mantine/core';
import { IconTrash, IconRobot, IconChevronDown, IconChevronUp, IconSettings, IconTools, IconFileExport } from '@tabler/icons-react';
import { motion } from 'framer-motion';
import { AgentPicker } from '../AgentPicker';
import { useAgents } from '@/lib/hooks/use-agents';
import type { StepData } from '../WorkflowStepList';
import { getDefaultAgentInputContextMode, type AgentInputContextMode } from '@/lib/types/workflow';

interface AgentNodeData {
  step: StepData;
  stepNumber: number;
  allSteps: StepData[];
  currentStepIndex: number;
  onChange: (updates: Partial<StepData>) => void;
  onRemove: () => void;
}

export const AgentNode = memo(({ data }: { data: AgentNodeData }) => {
  const { step, stepNumber, allSteps, currentStepIndex, onChange, onRemove } = data;
  const [expanded, setExpanded] = useState(false);
  const [advancedOpen, setAdvancedOpen] = useState(false);
  const { data: agents } = useAgents();

  const selectedAgent = agents?.find((a) => a.id === step.agentDefinitionId);
  const toolCount = selectedAgent?.availableTools?.length ?? 0;
  const inferredMode = getDefaultAgentInputContextMode(selectedAgent?.agentType);
  const effectiveMode: AgentInputContextMode = step.agentInputContextMode ?? inferredMode;
  const isInferredMode = step.agentInputContextMode == null;

  const priorStepOptions = allSteps
    .slice(0, currentStepIndex)
    .map((previousStep, index) => {
      const stepOrder = index + 1;
      const label = previousStep.label?.trim() || `Step ${stepOrder}`;
      return {
        value: String(stepOrder),
        label: `${stepOrder}. ${label}`,
      };
    });

  return (
    <>
      <Handle type="target" position={Position.Top} style={{ background: '#8b5cf6' }} />
      
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
            width: 320,
            border: '2px solid #8b5cf6',
            background: 'linear-gradient(135deg, #ffffff 0%, #f8f4ff 100%)',
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
                    background: 'linear-gradient(135deg, #8b5cf6 0%, #6d28d9 100%)',
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
                  gradient={{ from: 'violet', to: 'purple', deg: 90 }}
                  leftSection={<IconRobot size={14} />}
                >
                  AGENT
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
              placeholder="Step label"
              value={step.label}
              onChange={(e) => onChange({ label: e.target.value })}
              size="sm"
              styles={{
                input: {
                  fontWeight: 600,
                  fontSize: 15,
                  border: 'none',
                  background: 'transparent',
                  padding: 0,
                },
              }}
              onClick={(e) => e.stopPropagation()}
            />

            {/* Expanded Content */}
            <motion.div
              initial={false}
              animate={{ height: expanded ? 'auto' : 0, opacity: expanded ? 1 : 0 }}
              transition={{ duration: 0.2 }}
              style={{ overflow: 'hidden' }}
            >
              <Stack gap="xs">
                <AgentPicker
                  value={step.agentDefinitionId}
                  onChange={(agentDefinitionId) => onChange({ agentDefinitionId })}
                />

                <Select
                  label="Agent Context"
                  size="xs"
                  value={effectiveMode}
                  data={[
                    { value: 'FullWorkflow', label: 'Full workflow context' },
                    { value: 'PreviousStepOnly', label: 'Previous step only' },
                    { value: 'SelectedPriorSteps', label: 'Selected prior steps' },
                    { value: 'CustomMappedSubset', label: 'Custom mapped subset' },
                  ]}
                  onChange={(value) => {
                    if (!value) return;
                    const nextMode = value as AgentInputContextMode;
                    onChange({
                      agentInputContextMode: nextMode,
                      selectedPriorStepOrders: nextMode === 'SelectedPriorSteps' ? step.selectedPriorStepOrders : [],
                    });
                  }}
                  onClick={(e) => e.stopPropagation()}
                />

                {isInferredMode && (
                  <Text size="xs" c="dimmed">
                    Inferred default from agent type: {effectiveMode}
                  </Text>
                )}

                {effectiveMode === 'SelectedPriorSteps' && (
                  <MultiSelect
                    label="Selected prior steps"
                    size="xs"
                    data={priorStepOptions}
                    value={step.selectedPriorStepOrders.map((n) => String(n))}
                    onChange={(values) => onChange({ selectedPriorStepOrders: values.map((value) => Number(value)).filter((n) => Number.isFinite(n)) })}
                    placeholder={priorStepOptions.length > 0 ? 'Select earlier steps' : 'No earlier steps available'}
                    disabled={priorStepOptions.length === 0}
                    onClick={(e) => e.stopPropagation()}
                  />
                )}

                {selectedAgent && (
                  <Group gap={6}>
                    {toolCount > 0 && (
                      <Tooltip label={selectedAgent.availableTools!.join(', ')} multiline maw={280} withArrow>
                        <Badge color="blue" variant="outline" size="xs" leftSection={<IconTools size={9} />}>
                          {toolCount} tool{toolCount !== 1 ? 's' : ''}
                        </Badge>
                      </Tooltip>
                    )}
                    {selectedAgent.generatesOutput && (
                      <Badge color="teal" variant="outline" size="xs" leftSection={<IconFileExport size={9} />}>
                        {selectedAgent.outputSchemaName ?? 'Output'}
                      </Badge>
                    )}
                  </Group>
                )}

                <Button
                  variant="subtle"
                  size="compact-xs"
                  leftSection={<IconSettings size={14} />}
                  rightSection={advancedOpen ? <IconChevronUp size={14} /> : <IconChevronDown size={14} />}
                  onClick={(e) => {
                    e.stopPropagation();
                    setAdvancedOpen(!advancedOpen);
                  }}
                  fullWidth
                >
                  Advanced Settings
                </Button>

                <Collapse in={advancedOpen}>
                  {effectiveMode === 'CustomMappedSubset' ? (
                    <TextInput
                      label="Input Mapping JSON"
                      placeholder='{"input": "$.some.path"}'
                      value={step.inputMappingJson || ''}
                      onChange={(e) => onChange({ inputMappingJson: e.target.value || null })}
                      size="xs"
                      onClick={(e) => e.stopPropagation()}
                    />
                  ) : (
                    <Text size="xs" c="dimmed">
                      Input mapping is used only in Custom mapped subset mode.
                    </Text>
                  )}
                </Collapse>
              </Stack>
            </motion.div>
          </Stack>
        </Card>
      </motion.div>

      <Handle type="source" position={Position.Bottom} style={{ background: '#8b5cf6' }} />
    </>
  );
});

AgentNode.displayName = 'AgentNode';
