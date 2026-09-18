import { Container } from '@/components/layout/Container';
import { Button } from '@/components/ui/Button';
import { buildMetadata } from '@/lib/seo';

export const metadata = buildMetadata({
  title: 'Page not found',
  description: 'The page you were looking for does not exist or may have moved.',
  path: '/404',
  noIndex: true,
});

export default function NotFound() {
  return (
    <div className="py-16 text-center sm:py-20 lg:py-28">
      <Container>
        <p className="font-mono text-sm font-bold uppercase tracking-eyebrow text-accent-strong">404</p>
        <h1 className="mt-2 font-display text-3xl font-bold uppercase tracking-[-0.02em] text-ink sm:text-4xl">
          Page not found
        </h1>
        <p className="mx-auto mt-4 max-w-xl text-lg text-ink-muted">
          This page doesn&apos;t exist, may have moved, or the link you followed is out of date.
          Here&apos;s where most people are headed.
        </p>

        <ul className="mx-auto mt-8 flex max-w-xl flex-col gap-3 sm:flex-row sm:flex-wrap sm:justify-center">
          <li>
            <Button href="/" variant="accent">
              Home
            </Button>
          </li>
          <li>
            <Button href="/features" variant="outline">
              What it does
            </Button>
          </li>
          <li>
            <Button href="/contact" variant="outline">
              Book a demo
            </Button>
          </li>
          <li>
            <Button href="/app/login" variant="outline">
              Sign in
            </Button>
          </li>
        </ul>
      </Container>
    </div>
  );
}
