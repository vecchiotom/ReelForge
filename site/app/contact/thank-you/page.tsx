import { Container } from '@/components/layout/Container';
import { Button } from '@/components/ui/Button';
import { Eyebrow } from '@/components/ui/Eyebrow';
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
          <Eyebrow as="div" className="justify-center">Message received</Eyebrow>
          <h1 className="mt-4 text-balance font-display text-[clamp(1.75rem,4.5vw,3rem)] font-bold uppercase leading-[0.95] tracking-[-0.02em] text-ink">
            Thanks, your message is on its way
          </h1>
          <p className="mt-4 text-lg text-ink-muted">
            Someone from our team will read it and get back to you. Our target response time is{' '}
            {RESPONSE_TIME_SLA}.
          </p>

          <div className="mt-10 flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-center">
            <Button href="/" variant="accent" size="lg" skew>
              Back to home
            </Button>
            <Button href="/features" variant="outline" size="lg">
              Explore features
            </Button>
          </div>
        </div>

        <div className="mx-auto mt-16 max-w-sm border-t border-line pt-8 text-center">
          <h2 className="font-mono text-[11px] uppercase tracking-eyebrow text-ink-muted">
            Need to reach us sooner?
          </h2>
          <ContactAddress showPhone showResponseTime className="mt-4 text-center" />
        </div>
      </Container>
    </div>
  );
}
