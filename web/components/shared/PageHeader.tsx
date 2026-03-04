'use client';

import { Group, Title, Breadcrumbs, Anchor, Stack } from '@mantine/core';
import Link from 'next/link';

interface Crumb {
  label: string;
  href?: string;
}

interface PageHeaderProps {
  title: string;
  breadcrumbs?: Crumb[];
  children?: React.ReactNode;
}

export function PageHeader({ title, breadcrumbs, children }: PageHeaderProps) {
  return (
    <Stack gap="xs" mb="lg">
      {breadcrumbs && breadcrumbs.length > 0 && (
        <Breadcrumbs>
          {breadcrumbs.map((crumb, i) =>
            crumb.href ? (
              <Anchor component={Link} href={crumb.href} key={i} size="sm">
                {crumb.label}
              </Anchor>
            ) : (
              <span key={i} style={{ fontSize: 'var(--mantine-font-size-sm)' }}>
                {crumb.label}
              </span>
            ),
          )}
        </Breadcrumbs>
      )}
      <Group justify="space-between" align="center">
        <Title order={2}>{title}</Title>
        {children && <Group gap="sm">{children}</Group>}
      </Group>
    </Stack>
  );
}
