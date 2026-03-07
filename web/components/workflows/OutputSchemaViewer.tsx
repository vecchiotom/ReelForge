'use client';

import { useMemo } from 'react';
import { Stack, Text, Accordion, Badge, Group, Code, Paper, CopyButton, ActionIcon, Tooltip } from '@mantine/core';
import { IconCopy, IconCheck } from '@tabler/icons-react';
import { useAgents } from '@/lib/hooks/use-agents';
import type { StepData } from './WorkflowStepList';

interface OutputSchemaViewerProps {
  previousSteps: StepData[];
  currentStepIndex: number;
}

interface SchemaField {
  name: string;
  type: string;
  path: string;
  description?: string;
  children?: SchemaField[];
}

/**
 * Parses the outputSchemaJson string into a tree of SchemaFields
 */
function parseSchemaJson(schemaJson: string | null): SchemaField[] {
  if (!schemaJson) return [];
  
  try {
    const schema = JSON.parse(schemaJson);
    
    // Handle the schema structure - could be various formats
    if (schema.properties) {
      return parseProperties(schema.properties, '');
    }
    
    // Fallback: create fields from schema keys
    return Object.entries(schema).map(([key, value]) => ({
      name: key,
      type: typeof value === 'object' ? 'object' : typeof value,
      path: key,
    }));
  } catch (error) {
    console.error('Failed to parse schema JSON:', error);
    return [];
  }
}

function parseProperties(properties: Record<string, any>, parentPath: string): SchemaField[] {
  return Object.entries(properties).map(([key, value]) => {
    const path = parentPath ? `${parentPath}.${key}` : key;
    const field: SchemaField = {
      name: key,
      type: value.type || 'unknown',
      path,
      description: value.description,
    };

    // Handle nested objects
    if (value.type === 'object' && value.properties) {
      field.children = parseProperties(value.properties, path);
    }

    // Handle arrays
    if (value.type === 'array' && value.items?.properties) {
      field.children = parseProperties(value.items.properties, `${path}[*]`);
    }

    return field;
  });
}

function SchemaFieldItem({ field, stepNumber }: { field: SchemaField; stepNumber: number }) {
  const expressionPath = `{{steps.${stepNumber}.${field.path}}}`;

  return (
    <Paper p="xs" withBorder>
      <Group justify="space-between" wrap="nowrap">
        <Stack gap={4} style={{ flex: 1 }}>
          <Group gap="xs">
            <Code style={{ fontSize: 12 }}>{field.name}</Code>
            <Badge size="xs" variant="light">{field.type}</Badge>
          </Group>
          {field.description && (
            <Text size="xs" c="dimmed">{field.description}</Text>
          )}
          <Code block style={{ fontSize: 11, padding: '4px 8px' }}>
            {expressionPath}
          </Code>
        </Stack>
        <CopyButton value={expressionPath} timeout={2000}>
          {({ copied, copy }) => (
            <Tooltip label={copied ? 'Copied' : 'Copy expression'}>
              <ActionIcon color={copied ? 'teal' : 'gray'} variant="subtle" onClick={copy} size="sm">
                {copied ? <IconCheck size={16} /> : <IconCopy size={16} />}
              </ActionIcon>
            </Tooltip>
          )}
        </CopyButton>
      </Group>
      {field.children && field.children.length > 0 && (
        <Stack gap="xs" ml="md" mt="xs">
          {field.children.map((child) => (
            <SchemaFieldItem key={child.name} field={child} stepNumber={stepNumber} />
          ))}
        </Stack>
      )}
    </Paper>
  );
}

export function OutputSchemaViewer({ previousSteps, currentStepIndex }: OutputSchemaViewerProps) {
  const { data: agents } = useAgents();

  const schemasData = useMemo(() => {
    if (!agents || !previousSteps.length) return [];

    return previousSteps
      .slice(0, currentStepIndex)
      .map((step, index) => {
        const agent = agents.find((a) => a.id === step.agentDefinitionId);
        if (!agent || !agent.outputSchemaJson) return null;

        const fields = parseSchemaJson(agent.outputSchemaJson);
        
        return {
          stepNumber: index + 1,
          stepLabel: step.label || `Step ${index + 1}`,
          agentName: agent.name,
          fields,
        };
      })
      .filter((s): s is NonNullable<typeof s> => s !== null);
  }, [agents, previousSteps, currentStepIndex]);

  if (schemasData.length === 0) {
    return (
      <Paper p="md" withBorder>
        <Text size="sm" c="dimmed" ta="center">
          No previous steps with output schemas available
        </Text>
      </Paper>
    );
  }

  return (
    <Stack gap="md">
      <Text size="sm" fw={600} c="dimmed">
        Available Output Fields from Previous Steps
      </Text>
      <Accordion variant="separated" radius="md">
        {schemasData.map((schema) => (
          <Accordion.Item key={schema.stepNumber} value={`step-${schema.stepNumber}`}>
            <Accordion.Control>
              <Group gap="xs">
                <Badge variant="filled" size="sm">{schema.stepNumber}</Badge>
                <Text fw={600} size="sm">{schema.stepLabel}</Text>
                <Text size="xs" c="dimmed">({schema.agentName})</Text>
              </Group>
            </Accordion.Control>
            <Accordion.Panel>
              <Stack gap="xs">
                {schema.fields.length > 0 ? (
                  schema.fields.map((field) => (
                    <SchemaFieldItem
                      key={field.name}
                      field={field}
                      stepNumber={schema.stepNumber}
                    />
                  ))
                ) : (
                  <Text size="xs" c="dimmed" ta="center">No schema fields available</Text>
                )}
              </Stack>
            </Accordion.Panel>
          </Accordion.Item>
        ))}
      </Accordion>
    </Stack>
  );
}
