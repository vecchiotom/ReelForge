import Link from 'next/link';
import { Container } from '@/components/layout/Container';
import { ContactAddress } from '@/components/contact/ContactAddress';
import { buildMetadata } from '@/lib/seo';
import { RESPONSE_TIME_SLA } from '@/lib/legal-placeholders';

export const metadata = buildMetadata({
  title: 'Thanks',
  description: 'Your message has been received. Our team will follow up with you soon about your ReelForge project.',
  path: '/contact/thank-you',
  noIndex: true,
});

export default function ContactThankYouPage() {
  return (
    <div className="py-16 sm:py-20 lg:py-28">
      <Container>
        <div className="mx-auto max-w-2xl text-center">
          <h1 className="text-3xl font-bold tracking-tight text-neutral-900 sm:text-4xl">
            Thanks — your message is on its way
          </h1>
          <p className="mt-4 text-lg text-neutral-600">
            Someone from our team will read it and get back to you. Our target response time is{' '}
            {RESPONSE_TIME_SLA}.
          </p>

          <div className="mt-10 flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-center">
            <Link
              href="/"
              className="inline-flex min-h-11 items-center justify-center rounded-md bg-brand-600 px-6 py-3 text-base font-semibold text-white hover:bg-brand-700"
            >
              Back to home
            </Link>
            <Link
              href="/features"
              className="inline-flex min-h-11 items-center justify-center rounded-md border border-neutral-300 px-6 py-3 text-base font-semibold text-neutral-700 hover:bg-neutral-50"
            >
              Explore features
            </Link>
          </div>
        </div>

        <div className="mx-auto mt-16 max-w-sm border-t border-neutral-200 pt-8 text-center">
          <h2 className="text-sm font-semibold text-neutral-900">
            Need to reach us sooner?
          </h2>
          <ContactAddress showPhone showResponseTime className="mt-4 text-center" />
        </div>
      </Container>
    </div>
  );
}
