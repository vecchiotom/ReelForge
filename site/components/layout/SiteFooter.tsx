import Link from 'next/link';
import type { ReactNode } from 'react';
import { siteConfig } from '@/lib/site-config';
import { ContactAddress } from '@/components/contact/ContactAddress';
import { CookiePreferencesLink } from '@/components/consent/CookiePreferencesLink';

function ColumnHeading({ children }: { children: ReactNode }) {
  return (
    <h2 className="font-mono text-[11px] uppercase tracking-eyebrow text-ink-muted">{children}</h2>
  );
}

function Lockup() {
  return (
    <Link href="/" className="flex items-center gap-2.5">
      <svg
        width="28"
        height="28"
        viewBox="0 0 28 28"
        fill="none"
        aria-hidden="true"
        focusable="false"
        className="shrink-0"
      >
        <rect width="28" height="28" rx="2" className="fill-accent-strong" />
        <path d="M9 8.5L19 14L9 19.5V8.5Z" fill="white" />
      </svg>
      <span className="flex flex-col leading-none">
        <span className="font-display text-base font-bold leading-none text-ink">{siteConfig.name}</span>
        <span className="mt-1 font-mono text-[10px] uppercase tracking-eyebrow text-ink-muted">
          {siteConfig.tagline}
        </span>
      </span>
    </Link>
  );
}

export function SiteFooter() {
  return (
    <footer className="border-t border-line bg-paper-2">
      <div className="mx-auto max-w-6xl px-4 sm:px-6 lg:px-8">
        <div className="grid grid-cols-1 border-line sm:grid-cols-2 sm:divide-x sm:divide-line lg:grid-cols-4">
          <div className="border-t border-line py-10 first:border-t-0 sm:px-8 sm:first:pl-0 lg:border-t-0">
            <ColumnHeading>Product</ColumnHeading>
            <ul className="mt-4 space-y-3">
              <li>
                <Link href="/features" className="text-sm text-ink-muted hover:text-ink">
                  Features
                </Link>
              </li>
            </ul>
          </div>

          <div className="border-t border-line py-10 sm:px-8 lg:border-t-0">
            <ColumnHeading>Company</ColumnHeading>
            <ul className="mt-4 space-y-3">
              <li>
                <Link href="/about" className="text-sm text-ink-muted hover:text-ink">
                  About
                </Link>
              </li>
              <li>
                <Link href="/contact" className="text-sm text-ink-muted hover:text-ink">
                  Contact
                </Link>
              </li>
            </ul>
          </div>

          <div className="border-t border-line py-10 sm:px-8 lg:border-t-0 lg:border-l">
            <ColumnHeading>Legal</ColumnHeading>
            <ul className="mt-4 space-y-3">
              <li>
                <Link href="/legal/privacy" className="text-sm text-ink-muted hover:text-ink">
                  Privacy policy
                </Link>
              </li>
              <li>
                <Link href="/legal/terms" className="text-sm text-ink-muted hover:text-ink">
                  Terms of service
                </Link>
              </li>
              <li>
                <CookiePreferencesLink className="text-sm" />
              </li>
            </ul>
          </div>

          <div className="border-t border-line py-10 sm:px-8 sm:last:pr-0 lg:border-t-0">
            <ColumnHeading>Contact</ColumnHeading>
            <ContactAddress className="mt-4" />
          </div>
        </div>

        <div className="flex flex-col gap-6 border-t border-line py-8 sm:flex-row sm:items-center sm:justify-between">
          <Lockup />
          <p className="font-mono text-xs text-ink-faint">
            &copy; {new Date().getFullYear()} {siteConfig.name}. All rights reserved.
          </p>
        </div>
      </div>
    </footer>
  );
}
