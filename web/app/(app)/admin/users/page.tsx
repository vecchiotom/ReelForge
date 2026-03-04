'use client';

import { useState } from 'react';
import { Button, Loader, Center } from '@mantine/core';
import { IconPlus } from '@tabler/icons-react';
import { notifications } from '@mantine/notifications';
import { useUsers } from '@/lib/hooks/use-admin-users';
import { deleteUser } from '@/lib/api/admin';
import { PageHeader } from '@/components/shared/PageHeader';
import { UserTable } from '@/components/admin/UserTable';
import { UserForm } from '@/components/admin/UserForm';
import { TempPasswordModal } from '@/components/admin/TempPasswordModal';
import { ConfirmModal } from '@/components/shared/ConfirmModal';
import { EmptyState } from '@/components/shared/EmptyState';
import type { AdminUser, CreateUserResponse } from '@/lib/types/admin';

export default function AdminUsersPage() {
  const { data: users, isLoading, mutate } = useUsers();
  const [formOpened, setFormOpened] = useState(false);
  const [tempPassword, setTempPassword] = useState<CreateUserResponse | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<AdminUser | null>(null);
  const [deleteLoading, setDeleteLoading] = useState(false);

  const handleCreateSuccess = (result?: CreateUserResponse) => {
    mutate();
    if (result) {
      setTempPassword(result);
    }
  };

  const handleDelete = async () => {
    if (!deleteTarget) return;
    setDeleteLoading(true);
    try {
      await deleteUser(deleteTarget.id);
      mutate();
      notifications.show({ title: 'Deleted', message: 'User deleted', color: 'green' });
    } catch {
      notifications.show({ title: 'Error', message: 'Failed to delete user', color: 'red' });
    } finally {
      setDeleteLoading(false);
      setDeleteTarget(null);
    }
  };

  if (isLoading) {
    return <Center h={300}><Loader /></Center>;
  }

  return (
    <>
      <PageHeader title="Users">
        <Button leftSection={<IconPlus size={16} />} onClick={() => setFormOpened(true)}>
          New User
        </Button>
      </PageHeader>

      {users && users.length > 0 ? (
        <UserTable users={users} onDelete={setDeleteTarget} />
      ) : (
        <EmptyState title="No users" description="Create your first user to get started." />
      )}

      <UserForm opened={formOpened} onClose={() => setFormOpened(false)} onSuccess={handleCreateSuccess} />

      {tempPassword && (
        <TempPasswordModal
          opened={!!tempPassword}
          onClose={() => setTempPassword(null)}
          email={tempPassword.user.email}
          password={tempPassword.temporaryPassword}
        />
      )}

      <ConfirmModal
        opened={!!deleteTarget}
        onClose={() => setDeleteTarget(null)}
        onConfirm={handleDelete}
        title="Delete User"
        message={`Are you sure you want to delete ${deleteTarget?.email}?`}
        loading={deleteLoading}
      />
    </>
  );
}
