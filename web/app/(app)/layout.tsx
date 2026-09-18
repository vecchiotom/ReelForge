import { AppShell } from '@/components/shell/AppShell';
import { ServiceWorkerRegistration } from '@/components/shell/ServiceWorkerRegistration';

export default function AppLayout({ children }: { children: React.ReactNode }) {
  return (
    <>
      <ServiceWorkerRegistration />
      <AppShell>{children}</AppShell>
    </>
  );
}
