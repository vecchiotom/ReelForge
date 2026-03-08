'use client';

import { useState } from 'react';
import { Modal, Textarea, Group, Button, Text } from '@mantine/core';

interface ExecuteWithInputModalProps {
  opened: boolean;
  onClose: () => void;
  onConfirm: (userRequest: string | null) => void;
  executing?: boolean;
}

export function ExecuteWithInputModal({ opened, onClose, onConfirm, executing }: ExecuteWithInputModalProps) {
  const [userRequest, setUserRequest] = useState('');

  const handleConfirm = () => {
    onConfirm(userRequest.trim() || null);
  };

  const handleClose = () => {
    setUserRequest('');
    onClose();
  };

  return (
    <Modal opened={opened} onClose={handleClose} title="Execute Workflow" centered size="md">
      <Text size="sm" c="dimmed" mb="md">
        Provide a free-text request that will be passed as context to all agents during execution.
      </Text>
      <Textarea
        label="User Request"
        placeholder="Describe what you want the workflow to produce..."
        minRows={4}
        autosize
        maxRows={10}
        value={userRequest}
        onChange={(e) => setUserRequest(e.currentTarget.value)}
        mb="lg"
      />
      <Group justify="flex-end">
        <Button variant="default" onClick={handleClose} disabled={executing}>
          Cancel
        </Button>
        <Button
          onClick={handleConfirm}
          loading={executing}
          variant="gradient"
          gradient={{ from: 'violet', to: 'purple' }}
        >
          Execute
        </Button>
      </Group>
    </Modal>
  );
}
