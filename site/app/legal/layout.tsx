import type { ReactNode } from 'react';
import { Container } from '@/components/layout/Container';
import { Section } from '@/components/layout/Section';
import { LAST_UPDATED } from '@/lib/legal-placeholders';

export default function LegalLayout({ children }: { children: ReactNode }) {
  return (
    <Section>
      <Container>
        <div className="mx-auto max-w-3xl">
          <p className="text-sm text-neutral-500">Last updated: {LAST_UPDATED}</p>
          <div className="mt-2">{children}</div>
          <p className="mt-16 border-t border-neutral-200 pt-6 text-xs text-neutral-400">
            This document contains bracketed placeholder tokens (like{' '}
            <code className="text-neutral-500">[COMPANY_LEGAL_NAME]</code>) that stand in for facts
            only the business owner can supply. It must be completed with real, accurate
            information before this site is used to serve real users.
          </p>
        </div>
      </Container>
    </Section>
  );
}
