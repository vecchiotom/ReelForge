import type { Metadata } from 'next';
import { Container } from '@/components/layout/Container';
import { ContactForm } from '@/components/contact/ContactForm';

export const metadata: Metadata = {
  title: 'Contact',
  description: 'Get in touch about using ReelForge for your project.',
};

export default function ContactPage() {
  return (
    <div className="py-16 sm:py-20 lg:py-28">
      <Container>
        <h1 className="text-3xl font-bold tracking-tight text-neutral-900 sm:text-4xl lg:text-5xl">
          Contact
        </h1>
        <p className="mt-4 max-w-2xl text-lg text-neutral-600">
          Tell us a bit about your project and what you&apos;d like to use ReelForge for — we&apos;ll
          get back to you.
        </p>

        <div className="mt-12 grid grid-cols-1 gap-12 lg:grid-cols-3">
          <div className="lg:col-span-2">
            <ContactForm />
          </div>

          <aside className="lg:col-span-1">
            <h2 className="text-sm font-semibold text-neutral-900">Other ways to reach us</h2>
            {/*
              Minimal/structural for now. Phase 4 replaces this mailto placeholder and
              adds the real postal address using the bracketed tokens from
              site/lib/legal-placeholders.ts.
            */}
            <address className="not-italic mt-4 space-y-1 text-sm text-neutral-600">
              <p>
                <a href="mailto:hello@example.com" className="text-brand-600 hover:text-brand-700">
                  hello@example.com
                </a>
              </p>
            </address>
          </aside>
        </div>
      </Container>
    </div>
  );
}
