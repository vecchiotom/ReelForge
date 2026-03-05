'use client';

import { useState } from 'react';
import { TextInput, PasswordInput, Button, Stack, Alert } from '@mantine/core';
import { useForm } from '@mantine/form';
import { IconAlertCircle } from '@tabler/icons-react';
import { login } from '@/lib/api/auth';
import { notifyAuthChange } from '@/lib/hooks/use-auth';
import { useRouter } from 'next/navigation';

export function LoginForm() {
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const router = useRouter();

  const form = useForm({
    initialValues: { email: '', password: '' },
    validate: {
      email: (v) => (!v ? 'Email is required' : null),
      password: (v) => (!v ? 'Password is required' : null),
    },
  });

  const handleSubmit = form.onSubmit(async (values) => {
    setError('');
    setLoading(true);
    try {
      const res = await login(values);
      // Cookies are set by the Go API via Set-Cookie headers
      notifyAuthChange();
      if (res.mustChangePassword) {
        router.push('/change-password');
      } else {
        router.push('/dashboard');
      }
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Login failed');
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
        <TextInput
          label="Email"
          placeholder="you@example.com"
          autoComplete="email"
          {...form.getInputProps('email')}
        />
        <PasswordInput
          label="Password"
          placeholder="Your password"
          autoComplete="current-password"
          {...form.getInputProps('password')}
        />
        <Button type="submit" loading={loading} fullWidth>
          Sign in
        </Button>
      </Stack>
    </form>
  );
}
