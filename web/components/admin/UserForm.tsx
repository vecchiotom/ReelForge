'use client';

import { useState } from 'react';
import { Modal, TextInput, Switch, Button, Stack } from '@mantine/core';
import { useForm } from '@mantine/form';
import { notifications } from '@mantine/notifications';
import { createUser, updateUser } from '@/lib/api/admin';
import type { AdminUser, CreateUserResponse } from '@/lib/types/admin';

interface UserFormProps {
  opened: boolean;
  onClose: () => void;
  onSuccess: (result?: CreateUserResponse) => void;
  user?: AdminUser;
}

export function UserForm({ opened, onClose, onSuccess, user }: UserFormProps) {
  const [loading, setLoading] = useState(false);
  const isEdit = !!user;

  const form = useForm({
    initialValues: {
      email: user?.email || '',
      displayName: user?.displayName || '',
      isAdmin: user?.isAdmin || false,
      resetPassword: false,
    },
    validate: {
      email: (v) => (!v.trim() ? 'Email is required' : null),
      displayName: (v) => (!v.trim() ? 'Display name is required' : null),
    },
  });

  const handleSubmit = form.onSubmit(async (values) => {
    setLoading(true);
    try {
      if (isEdit) {
        await updateUser(user.id, {
          email: values.email,
          displayName: values.displayName,
          isAdmin: values.isAdmin,
          resetPassword: values.resetPassword || undefined,
        });
        notifications.show({ title: 'Updated', message: 'User updated', color: 'green' });
        onSuccess();
      } else {
        const result = await createUser({
          email: values.email,
          displayName: values.displayName,
          isAdmin: values.isAdmin,
        });
        onSuccess(result);
      }
      form.reset();
      onClose();
    } catch (err: unknown) {
      notifications.show({
        title: 'Error',
        message: err instanceof Error ? err.message : 'Operation failed',
        color: 'red',
      });
    } finally {
      setLoading(false);
    }
  });

  return (
    <Modal opened={opened} onClose={onClose} title={isEdit ? 'Edit User' : 'Create User'} centered>
      <form onSubmit={handleSubmit}>
        <Stack>
          <TextInput label="Email" placeholder="user@example.com" {...form.getInputProps('email')} />
          <TextInput label="Display Name" placeholder="John Doe" {...form.getInputProps('displayName')} />
          <Switch label="Administrator" {...form.getInputProps('isAdmin', { type: 'checkbox' })} />
          {isEdit && (
            <Switch
              label="Reset password (generates new temporary password)"
              {...form.getInputProps('resetPassword', { type: 'checkbox' })}
            />
          )}
          <Button type="submit" loading={loading} fullWidth>
            {isEdit ? 'Update' : 'Create'}
          </Button>
        </Stack>
      </form>
    </Modal>
  );
}
