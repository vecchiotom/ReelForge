'use client';

import { Alert } from '@mantine/core';
import { IconAlertCircle } from '@tabler/icons-react';

interface ErrorAlertProps {
  message: string;
}

export function ErrorAlert({ message }: ErrorAlertProps) {
  return (
    <Alert icon={<IconAlertCircle size={16} />} color="red" variant="light">
      {message}
    </Alert>
  );
}
