'use client';

import { Badge } from '@mantine/core';
import { AGENT_CATEGORY_COLORS } from '@/lib/utils/constants';

export function AgentTypeBadge({ category }: { category: string }) {
  return (
    <Badge color={AGENT_CATEGORY_COLORS[category] || 'gray'} variant="light" size="sm">
      {category}
    </Badge>
  );
}
