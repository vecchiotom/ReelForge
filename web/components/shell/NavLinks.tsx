'use client';

import { NavLink } from '@mantine/core';
import {
  IconDashboard,
  IconFolder,
  IconRobot,
  IconUsers,
  IconSettings,
} from '@tabler/icons-react';
import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { useAuth } from '@/lib/hooks/use-auth';

const links = [
  { label: 'Dashboard', href: '/dashboard', icon: IconDashboard },
  { label: 'Projects', href: '/projects', icon: IconFolder },
  { label: 'Agents', href: '/agents', icon: IconRobot },
];

const adminLinks = [
  { label: 'Overview', href: '/admin', icon: IconSettings, exact: true },
  { label: 'Users', href: '/admin/users', icon: IconUsers, exact: false },
];

export function NavLinks({ onClick }: { onClick?: () => void }) {
  const pathname = usePathname();
  const { isAdmin } = useAuth();

  return (
    <>
      {links.map((link) => (
        <NavLink
          key={link.href}
          component={Link}
          href={link.href}
          label={link.label}
          leftSection={<link.icon size={20} />}
          active={pathname.startsWith(link.href)}
          onClick={onClick}
        />
      ))}
      {isAdmin && (
        <>
          <NavLink label="Admin" disabled styles={{ label: { fontWeight: 700, fontSize: 'var(--mantine-font-size-xs)', textTransform: 'uppercase' } }} />
          {adminLinks.map((link) => (
            <NavLink
              key={link.href}
              component={Link}
              href={link.href}
              label={link.label}
              leftSection={<link.icon size={20} />}
              active={link.exact ? pathname === link.href : pathname.startsWith(link.href)}
              onClick={onClick}
            />
          ))}
        </>
      )}
    </>
  );
}
