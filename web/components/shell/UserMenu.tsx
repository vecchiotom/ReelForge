'use client';

import { Menu, UnstyledButton, Group, Avatar, Text } from '@mantine/core';
import { IconLogout, IconUser } from '@tabler/icons-react';
import { useAuth } from '@/lib/hooks/use-auth';
import { ThemeToggle } from './ThemeToggle';

export function UserMenu() {
  const { user, logout } = useAuth();

  return (
    <Group gap="sm">
      <ThemeToggle />
      <Menu shadow="md" width={200} position="bottom-end">
        <Menu.Target>
          <UnstyledButton>
            <Group gap="xs">
              <Avatar size="sm" radius="xl" color="violet">
                <IconUser size={16} />
              </Avatar>
              <Text size="sm" fw={500} visibleFrom="sm">
                {user?.email}
              </Text>
            </Group>
          </UnstyledButton>
        </Menu.Target>
        <Menu.Dropdown>
          <Menu.Label>{user?.email}</Menu.Label>
          <Menu.Item leftSection={<IconLogout size={14} />} onClick={logout} color="red">
            Logout
          </Menu.Item>
        </Menu.Dropdown>
      </Menu>
    </Group>
  );
}
