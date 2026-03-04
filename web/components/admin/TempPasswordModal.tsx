'use client';

import { Modal, Text, Code, Stack, Button, CopyButton } from '@mantine/core';
import { IconCopy, IconCheck } from '@tabler/icons-react';

interface TempPasswordModalProps {
  opened: boolean;
  onClose: () => void;
  email: string;
  password: string;
}

export function TempPasswordModal({ opened, onClose, email, password }: TempPasswordModalProps) {
  return (
    <Modal opened={opened} onClose={onClose} title="User Created" centered>
      <Stack gap="md">
        <Text size="sm">
          User <strong>{email}</strong> has been created with a temporary password:
        </Text>
        <Code block p="md" style={{ fontSize: 'var(--mantine-font-size-lg)', textAlign: 'center' }}>
          {password}
        </Code>
        <Text size="xs" c="dimmed">
          The user will be required to change this password on first login.
        </Text>
        <CopyButton value={password}>
          {({ copied, copy }) => (
            <Button
              leftSection={copied ? <IconCheck size={16} /> : <IconCopy size={16} />}
              color={copied ? 'teal' : 'violet'}
              onClick={copy}
              fullWidth
            >
              {copied ? 'Copied!' : 'Copy Password'}
            </Button>
          )}
        </CopyButton>
      </Stack>
    </Modal>
  );
}
