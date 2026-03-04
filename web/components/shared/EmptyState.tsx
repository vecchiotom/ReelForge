'use client';

import { Stack, Text, ThemeIcon } from '@mantine/core';
import { IconInbox } from '@tabler/icons-react';

interface EmptyStateProps {
  title: string;
  description?: string;
  children?: React.ReactNode;
}

export function EmptyState({ title, description, children }: EmptyStateProps) {
  return (
    <Stack align="center" py="xl" gap="md">
      <ThemeIcon size={64} variant="light" color="gray" radius="xl">
        <IconInbox size={32} />
      </ThemeIcon>
      <Text fw={500} size="lg">
        {title}
      </Text>
      {description && (
        <Text size="sm" c="dimmed" maw={400} ta="center">
          {description}
        </Text>
      )}
      {children}
    </Stack>
  );
}
