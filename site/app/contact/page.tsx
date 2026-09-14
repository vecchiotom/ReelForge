import { Container } from '@/components/layout/Container';
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
            <ContactAddress showPhone showResponseTime className="mt-4" />
          </aside>
        </div>
      </Container>
    </div>
  );
}
