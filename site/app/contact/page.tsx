import { Container } from '@/components/layout/Container';
import { GridOverlay } from '@/components/ui/GridOverlay';
import { Eyebrow } from '@/components/ui/Eyebrow';
import { Panel } from '@/components/ui/Panel';
import { ContactForm } from '@/components/contact/ContactForm';
import { ContactAddress } from '@/components/contact/ContactAddress';
import { buildMetadata } from '@/lib/seo';

export const metadata = buildMetadata({
  title: 'Contact',
  description:
    'Tell us about your project and what you want to use ReelForge for — reach our team directly and we will get back to you shortly.',
  path: '/contact',
});

export default function ContactPage() {
  return (
    <div className="relative isolate overflow-hidden py-16 sm:py-20 lg:py-28">
      <GridOverlay columns={3} className="hidden lg:block" />
      <Container>
        <Eyebrow>Talk to us</Eyebrow>
        <h1 className="mt-4 text-balance font-display text-[clamp(2rem,5vw,3.5rem)] font-bold uppercase leading-[0.95] tracking-[-0.02em] text-ink">
          Contact
        </h1>
        <p className="mt-4 max-w-2xl font-mono text-sm leading-relaxed text-ink-muted">
          Tell us a bit about your project and what you&apos;d like to use ReelForge for — we&apos;ll
          get back to you.
        </p>

        <div className="mt-12 grid grid-cols-1 gap-10 lg:grid-cols-3 lg:gap-12">
          <div className="lg:col-span-2">
            <ContactForm />
          </div>

          <aside className="lg:col-span-1">
            {/* Real heading, not the decorative Eyebrow component — styled to match it visually. */}
            <Panel brackets className="p-6">
              <h2 className="flex items-center gap-2 font-mono text-[11px] uppercase tracking-eyebrow text-ink-muted">
                <span aria-hidden="true" className="h-2 w-2 shrink-0 bg-accent" />
                Other ways to reach us
              </h2>
              <ContactAddress showPhone showResponseTime className="mt-4" />
            </Panel>
          </aside>
        </div>
      </Container>
    </div>
  );
}
