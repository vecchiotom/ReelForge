import type { Metadata } from 'next';
import Link from 'next/link';
import { Container } from '@/components/layout/Container';

export const metadata: Metadata = {
  title: 'Thanks',
  description: 'Your message has been received.',
};

// Placeholder for now — Phase 4 finishes this page properly.
export default function ContactThankYouPage() {
  return (
    <div className="py-16 text-center sm:py-20 lg:py-28">
      <Container>
        <h1 className="text-3xl font-bold tracking-tight text-neutral-900 sm:text-4xl">
          Thanks — we got your message
        </h1>
        <p className="mt-4 text-lg text-neutral-600">We&apos;ll be in touch soon.</p>
        <Link
          href="/"
          className="mt-8 inline-flex min-h-11 items-center justify-center rounded-md bg-brand-600 px-6 py-3 text-base font-semibold text-white hover:bg-brand-700"
        >
          Back to home
        </Link>
      </Container>
    </div>
  );
}
