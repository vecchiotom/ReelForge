'use client';

import { use, useState } from 'react';
import { Card, Text, Stack, Group, Badge, Button, Loader, Center } from '@mantine/core';
import { IconEdit, IconTrash } from '@tabler/icons-react';
import { useUser } from '@/lib/hooks/use-admin-users';
import { deleteUser } from '@/lib/api/admin';
import { PageHeader } from '@/components/shared/PageHeader';
import { UserForm } from '@/components/admin/UserForm';
import { ConfirmModal } from '@/components/shared/ConfirmModal';
import { formatDate } from '@/lib/utils/format';
import { notifications } from '@mantine/notifications';
import { useRouter } from 'next/navigation';

export default function AdminUserDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const { data: user, isLoading, mutate } = useUser(id);
  const router = useRouter();
  const [editOpened, setEditOpened] = useState(false);
  const [deleteOpened, setDeleteOpened] = useState(false);
  const [deleteLoading, setDeleteLoading] = useState(false);

  if (isLoading) return <Center h={300}><Loader /></Center>;
  if (!user) return <Text>User not found</Text>;

  const handleDelete = async () => {
    setDeleteLoading(true);
    try {
      await deleteUser(id);
      notifications.show({ title: 'Deleted', message: 'User deleted', color: 'green' });
      router.push('/admin/users');
    } catch {
      notifications.show({ title: 'Error', message: 'Failed to delete user', color: 'red' });
    } finally {
      setDeleteLoading(false);
    }
  };

  return (
    <>
      <PageHeader
        title={user.displayName || user.email}
        breadcrumbs={[{ label: 'Users', href: '/admin/users' }, { label: user.email }]}
      >
        <Button variant="default" leftSection={<IconEdit size={16} />} onClick={() => setEditOpened(true)}>
          Edit
        </Button>
        <Button color="red" variant="outline" leftSection={<IconTrash size={16} />} onClick={() => setDeleteOpened(true)}>
          Delete
        </Button>
      </PageHeader>

      <Card withBorder>
        <Stack gap="sm">
          <Group>
            <Text size="sm" fw={500} w={120}>Email</Text>
            <Text size="sm">{user.email}</Text>
          </Group>
          <Group>
            <Text size="sm" fw={500} w={120}>Display Name</Text>
            <Text size="sm">{user.displayName}</Text>
          </Group>
          <Group>
            <Text size="sm" fw={500} w={120}>Role</Text>
            <Badge color={user.isAdmin ? 'violet' : 'gray'} variant="light">
              {user.isAdmin ? 'Admin' : 'User'}
            </Badge>
          </Group>
          <Group>
            <Text size="sm" fw={500} w={120}>Status</Text>
            {user.mustChangePassword ? (
              <Badge color="orange" variant="light">Must change password</Badge>
            ) : (
              <Badge color="green" variant="light">Active</Badge>
            )}
          </Group>
          <Group>
            <Text size="sm" fw={500} w={120}>Created</Text>
            <Text size="sm">{formatDate(user.createdAt)}</Text>
          </Group>
        </Stack>
      </Card>

      <UserForm opened={editOpened} onClose={() => setEditOpened(false)} onSuccess={() => mutate()} user={user} />
      <ConfirmModal
        opened={deleteOpened}
        onClose={() => setDeleteOpened(false)}
        onConfirm={handleDelete}
        title="Delete User"
        message={`Are you sure you want to delete ${user.email}? This action cannot be undone.`}
        loading={deleteLoading}
      />
    </>
  );
}
