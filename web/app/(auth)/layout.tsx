import { Center, Container } from '@mantine/core';

export default function AuthLayout({ children }: { children: React.ReactNode }) {
  return (
    <Center mih="100vh">
      <Container size={420} w="100%">
        {children}
      </Container>
    </Center>
  );
}
