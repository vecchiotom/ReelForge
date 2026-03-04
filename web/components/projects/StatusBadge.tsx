'use client';

import { Badge } from '@mantine/core';
import { STATUS_COLORS } from '@/lib/utils/constants';

export function StatusBadge({ status }: { status: string }) {
  return (
    <Badge color={STATUS_COLORS[status] || 'gray'} variant="light" size="sm">
      {status}
    </Badge>
  );
}
