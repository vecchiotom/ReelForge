import { Card, Title, Text, Stack } from '@mantine/core';
import { LoginForm } from '@/components/auth/LoginForm';

export default function LoginPage() {
  return (
    <Card shadow="md" radius="md" p="xl" withBorder>
      <Stack gap="md" mb="md">
        <Title order={2} ta="center">
          ReelForge
        </Title>
        <Text size="sm" ta="center" c="dimmed">
          Sign in to your account
        </Text>
      </Stack>
      <LoginForm />
    </Card>
  );
}
