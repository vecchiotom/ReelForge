'use client';

import { Stack, Text, Badge, Group, Code, Paper, Accordion } from '@mantine/core';
import { IconChevronRight } from '@tabler/icons-react';

interface AgentSchemaViewerProps {
  schemaJson: string;
}

interface SchemaField {
  name: string;
  type: string;
  path: string;
  description?: string;
  children?: SchemaField[];
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

    if (value.type === 'object' && value.properties) {
      field.children = parseProperties(value.properties, path);
    }

    if (value.type === 'array' && value.items?.properties) {
      field.children = parseProperties(value.items.properties, `${path}[*]`);
    }

    return field;
  });
}

function parseSchemaJson(schemaJson: string): SchemaField[] {
  try {
    const schema = JSON.parse(schemaJson);
    if (schema.properties) {
      return parseProperties(schema.properties, '');
    }
    return Object.entries(schema).map(([key, value]) => ({
      name: key,
      type: typeof value === 'object' ? 'object' : typeof value,
      path: key,
    }));
  } catch {
    return [];
  }
}

function typeColor(type: string): string {
  switch (type) {
    case 'string': return 'blue';
    case 'integer':
    case 'number': return 'orange';
    case 'boolean': return 'grape';
    case 'array': return 'teal';
    case 'object': return 'violet';
    default: return 'gray';
  }
}

function SchemaFieldItem({ field, depth = 0 }: { field: SchemaField; depth?: number }) {
  const hasChildren = field.children && field.children.length > 0;

  if (hasChildren) {
    return (
      <Accordion variant="contained" radius="sm">
        <Accordion.Item value={field.path}>
          <Accordion.Control icon={<IconChevronRight size={12} />}>
            <Group gap="xs" wrap="nowrap">
              <Code style={{ fontSize: 12 }}>{field.name}</Code>
              <Badge size="xs" color={typeColor(field.type)} variant="light">{field.type}</Badge>
              {field.description && (
                <Text size="xs" c="dimmed" truncate style={{ flex: 1 }}>{field.description}</Text>
              )}
            </Group>
          </Accordion.Control>
          <Accordion.Panel>
            <Stack gap="xs">
              {field.children!.map((child) => (
                <SchemaFieldItem key={child.path} field={child} depth={depth + 1} />
              ))}
            </Stack>
          </Accordion.Panel>
        </Accordion.Item>
      </Accordion>
    );
  }

  return (
    <Paper p="xs" withBorder>
      <Group gap="xs" wrap="nowrap">
        <Code style={{ fontSize: 12 }}>{field.name}</Code>
        <Badge size="xs" color={typeColor(field.type)} variant="light">{field.type}</Badge>
        {field.description && (
          <Text size="xs" c="dimmed" style={{ flex: 1 }}>{field.description}</Text>
        )}
      </Group>
    </Paper>
  );
}

export function AgentSchemaViewer({ schemaJson }: AgentSchemaViewerProps) {
  const fields = parseSchemaJson(schemaJson);

  if (fields.length === 0) {
    return (
      <Paper p="md" withBorder>
        <Text size="sm" c="dimmed" ta="center">No schema fields available</Text>
      </Paper>
    );
  }

  return (
    <Stack gap="xs">
      {fields.map((field) => (
        <SchemaFieldItem key={field.path} field={field} />
      ))}
    </Stack>
  );
}
