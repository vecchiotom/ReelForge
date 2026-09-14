import Link from 'next/link';
import { Container } from '@/components/layout/Container';
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
        <p className="text-sm font-semibold text-brand-600">404</p>
        <h1 className="mt-2 text-3xl font-bold tracking-tight text-neutral-900 sm:text-4xl">
          Page not found
        </h1>
        <p className="mx-auto mt-4 max-w-xl text-lg text-neutral-600">
          The page you&apos;re looking for doesn&apos;t exist, may have moved, or the link you
          followed might be out of date.
        </p>

        <ul className="mx-auto mt-8 flex max-w-xl flex-col gap-3 sm:flex-row sm:flex-wrap sm:justify-center">
          <li>
            <Link
              href="/"
              className="inline-flex min-h-11 items-center justify-center rounded-md bg-brand-600 px-6 py-3 text-base font-semibold text-white hover:bg-brand-700"
            >
              Home
            </Link>
          </li>
          <li>
            <Link
              href="/features"
              className="inline-flex min-h-11 items-center justify-center rounded-md border border-neutral-300 px-6 py-3 text-base font-semibold text-neutral-700 hover:bg-neutral-50"
            >
              Features
            </Link>
          </li>
          <li>
            <Link
              href="/contact"
              className="inline-flex min-h-11 items-center justify-center rounded-md border border-neutral-300 px-6 py-3 text-base font-semibold text-neutral-700 hover:bg-neutral-50"
            >
              Contact
            </Link>
          </li>
          <li>
            <a
              href="/app/login"
              className="inline-flex min-h-11 items-center justify-center rounded-md border border-neutral-300 px-6 py-3 text-base font-semibold text-neutral-700 hover:bg-neutral-50"
            >
              Sign in
            </a>
          </li>
        </ul>
      </Container>
    </div>
  );
}
