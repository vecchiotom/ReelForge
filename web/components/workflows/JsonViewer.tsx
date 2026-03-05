'use client';

import { useState } from 'react';
import { Code, Button, Group, CopyButton, ActionIcon, Tooltip } from '@mantine/core';
import { IconChevronDown, IconChevronRight, IconCopy, IconCheck } from '@tabler/icons-react';

interface JsonViewerProps {
  label: string;
  value: string | null | undefined;
}

export function JsonViewer({ label, value }: JsonViewerProps) {
  const [expanded, setExpanded] = useState(false);

  if (!value) return null;

  let formatted: string;
  try {
    formatted = JSON.stringify(JSON.parse(value), null, 2);
  } catch {
    formatted = value;
  }

  return (
    <div>
      <Group gap="xs">
        <Button
          variant="subtle"
          size="compact-xs"
          leftSection={expanded ? <IconChevronDown size={14} /> : <IconChevronRight size={14} />}
          onClick={() => setExpanded(!expanded)}
        >
          {label}
        </Button>
        {expanded && (
          <CopyButton value={formatted}>
            {({ copied, copy }) => (
              <Tooltip label={copied ? 'Copied' : 'Copy'}>
                <ActionIcon variant="subtle" size="xs" onClick={copy}>
                  {copied ? <IconCheck size={12} /> : <IconCopy size={12} />}
                </ActionIcon>
              </Tooltip>
            )}
          </CopyButton>
        )}
      </Group>
      {expanded && (
        <Code block style={{ whiteSpace: 'pre-wrap', maxHeight: 300, overflow: 'auto' }} mt={4}>
          {formatted}
        </Code>
      )}
    </div>
  );
}
