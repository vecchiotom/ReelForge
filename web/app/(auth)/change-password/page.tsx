import { Card, Title, Text, Stack } from '@mantine/core';
import { ChangePasswordForm } from '@/components/auth/ChangePasswordForm';

export default function ChangePasswordPage() {
  return (
    <Card shadow="md" radius="md" p="xl" withBorder>
      <Stack gap="md" mb="md">
        <Title order={2} ta="center">
          Change Password
        </Title>
        <Text size="sm" ta="center" c="dimmed">
          You must change your password before continuing
        </Text>
      </Stack>
      <ChangePasswordForm />
    </Card>
  );
}
