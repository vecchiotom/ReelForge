'use client';

import { Table, Badge, ActionIcon, Group } from '@mantine/core';
import { IconEdit, IconTrash } from '@tabler/icons-react';
import { formatDate } from '@/lib/utils/format';
import type { AdminUser } from '@/lib/types/admin';
import Link from 'next/link';

interface UserTableProps {
  users: AdminUser[];
  onDelete: (user: AdminUser) => void;
}

export function UserTable({ users, onDelete }: UserTableProps) {
  return (
    <Table striped highlightOnHover>
      <Table.Thead>
        <Table.Tr>
          <Table.Th>Email</Table.Th>
          <Table.Th>Display Name</Table.Th>
          <Table.Th>Role</Table.Th>
          <Table.Th>Status</Table.Th>
          <Table.Th>Created</Table.Th>
          <Table.Th />
        </Table.Tr>
      </Table.Thead>
      <Table.Tbody>
        {users.map((user) => (
          <Table.Tr key={user.id}>
            <Table.Td>{user.email}</Table.Td>
            <Table.Td>{user.displayName}</Table.Td>
            <Table.Td>
              <Badge color={user.isAdmin ? 'violet' : 'gray'} variant="light" size="sm">
                {user.isAdmin ? 'Admin' : 'User'}
              </Badge>
            </Table.Td>
            <Table.Td>
              {user.mustChangePassword && (
                <Badge color="orange" variant="light" size="sm">
                  Must change password
                </Badge>
              )}
            </Table.Td>
            <Table.Td>{formatDate(user.createdAt)}</Table.Td>
            <Table.Td>
              <Group justify="flex-end" gap="xs">
                <ActionIcon component={Link} href={`/admin/users/${user.id}`} variant="subtle">
                  <IconEdit size={16} />
                </ActionIcon>
                <ActionIcon color="red" variant="subtle" onClick={() => onDelete(user)}>
                  <IconTrash size={16} />
                </ActionIcon>
              </Group>
            </Table.Td>
          </Table.Tr>
        ))}
      </Table.Tbody>
    </Table>
  );
}
