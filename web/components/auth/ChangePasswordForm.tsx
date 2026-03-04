'use client';

import { useState } from 'react';
import { PasswordInput, Button, Stack, Alert } from '@mantine/core';
import { useForm } from '@mantine/form';
import { IconAlertCircle } from '@tabler/icons-react';
import { changePassword } from '@/lib/api/auth';
import { notifyAuthChange } from '@/lib/hooks/use-auth';
import { useRouter } from 'next/navigation';

export function ChangePasswordForm() {
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const router = useRouter();

  const form = useForm({
    initialValues: { currentPassword: '', newPassword: '', confirmPassword: '' },
    validate: {
      currentPassword: (v) => (!v ? 'Current password is required' : null),
      newPassword: (v) => (v.length < 8 ? 'Password must be at least 8 characters' : null),
      confirmPassword: (v, values) => (v !== values.newPassword ? 'Passwords do not match' : null),
    },
  });

  const handleSubmit = form.onSubmit(async (values) => {
    setError('');
    setLoading(true);
    try {
      await changePassword({
        currentPassword: values.currentPassword,
        newPassword: values.newPassword,
      });
      // Update the cookie to clear mustChangePassword
      const userCookie = document.cookie.match(/reelforge_user=([^;]+)/);
      if (userCookie) {
        try {
          const user = JSON.parse(decodeURIComponent(userCookie[1]));
          user.mustChangePassword = false;
          document.cookie = `reelforge_user=${encodeURIComponent(JSON.stringify(user))}; Path=/; SameSite=Lax; Max-Age=86400`;
        } catch {}
      }
      notifyAuthChange();
      router.push('/dashboard');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to change password');
    } finally {
      setLoading(false);
    }
  });

  return (
    <form onSubmit={handleSubmit}>
      <Stack>
        {error && (
          <Alert icon={<IconAlertCircle size={16} />} color="red" variant="light">
            {error}
          </Alert>
        )}
        <PasswordInput
          label="Current Password"
          placeholder="Your current password"
          {...form.getInputProps('currentPassword')}
        />
        <PasswordInput
          label="New Password"
          placeholder="At least 8 characters"
          {...form.getInputProps('newPassword')}
        />
        <PasswordInput
          label="Confirm New Password"
          placeholder="Repeat new password"
          {...form.getInputProps('confirmPassword')}
        />
        <Button type="submit" loading={loading} fullWidth>
          Change Password
        </Button>
      </Stack>
    </form>
  );
}
