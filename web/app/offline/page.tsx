import { Card, Center, Container, Stack, Text, Title, ThemeIcon } from '@mantine/core';
import { IconWifiOff } from '@tabler/icons-react';

// Served by the service worker (public/sw.js) as the fallback for a failed page navigation while
// offline. ReelForge is a live agent platform — execution progress streams over SSE, file uploads
// go straight to storage — so there's no meaningful offline mode to offer here. This page says
// that plainly instead of implying the dashboard works without a connection.
export default function OfflinePage() {
  return (
    <Center mih="100vh">
      <Container size={420} w="100%">
        <Card shadow="md" radius="md" p="xl" withBorder>
          <Stack gap="md" align="center">
            <ThemeIcon size={64} variant="light" color="gray" radius="xl">
              <IconWifiOff size={32} />
            </ThemeIcon>
            <Title order={2} ta="center">
              You&apos;re offline
            </Title>
            <Text size="sm" c="dimmed" ta="center">
              ReelForge needs a connection. Workflow execution, live progress updates, and file
              uploads all happen on the server in real time, so there&apos;s no offline mode to
              fall back to here — reconnect and reload to keep working.
            </Text>
          </Stack>
        </Card>
      </Container>
    </Center>
  );
}
