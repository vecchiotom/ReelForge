import type { ReactNode } from 'react';
import { Container } from './Container';

export function Section({
  id,
  className,
  children,
}: {
  id?: string;
  className?: string;
  children: ReactNode;
}) {
  return (
    <section id={id} className={`py-16 sm:py-20 lg:py-28 ${className ?? ''}`.trim()}>
      <Container>{children}</Container>
    </section>
  );
}
