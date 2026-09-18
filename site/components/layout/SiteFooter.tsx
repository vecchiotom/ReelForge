import Link from 'next/link';
import type { ReactNode } from 'react';
import { siteConfig } from '@/lib/site-config';
import { ContactAddress } from '@/components/contact/ContactAddress';
import { CookiePreferencesLink } from '@/components/consent/CookiePreferencesLink';

// In-page anchors on the homepage are deliberate: they are the deepest real entry points the
// site has, and linking them here gives crawlers (and readers arriving on a legal page) a route
// straight back into the sections that explain the product.
const PRODUCT_LINKS = [
  { label: 'How it works', href: '/#how-it-works' },
  { label: 'Features', href: '/features' },
  { label: 'What you get', href: '/#whats-inside' },
  { label: 'FAQ', href: '/#faq' },
] as const;

// Deliberately a <p>, not a heading. These four labels repeat on every page of the site; as <h2>
// elements they sat in the document outline alongside each page's real section headings and
// diluted it. The landmark role and its accessible name come from the wrapping <nav
// aria-labelledby>, which is what actually lets assistive tech jump between footer groups.
function ColumnHeading({ id, children }: { id: string; children: ReactNode }) {
  return (
    <p id={id} className="font-mono text-[11px] uppercase tracking-eyebrow text-ink-muted">
      {children}
    </p>
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
            <ColumnHeading id="footer-product">Product</ColumnHeading>
            <nav aria-labelledby="footer-product">
              <ul className="mt-4 space-y-3">
                {PRODUCT_LINKS.map((link) => (
                  <li key={link.href}>
                    <Link href={link.href} className="text-sm text-ink-muted hover:text-ink">
                      {link.label}
                    </Link>
                  </li>
                ))}
              </ul>
            </nav>
          </div>

          <div className="border-t border-line py-10 sm:px-8 lg:border-t-0">
            <ColumnHeading id="footer-company">Company</ColumnHeading>
            <nav aria-labelledby="footer-company">
              <ul className="mt-4 space-y-3">
                <li>
                  <Link href="/about" className="text-sm text-ink-muted hover:text-ink">
                    About
                  </Link>
                </li>
                <li>
                  <Link href="/contact" className="text-sm text-ink-muted hover:text-ink">
                    Book a demo
                  </Link>
                </li>
                <li>
                  <a href="/app/login" className="text-sm text-ink-muted hover:text-ink">
                    Sign in
                  </a>
                </li>
              </ul>
            </nav>
          </div>

          <div className="border-t border-line py-10 sm:px-8 lg:border-t-0 lg:border-l">
            <ColumnHeading id="footer-legal">Legal</ColumnHeading>
            <nav aria-labelledby="footer-legal">
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
            </nav>
          </div>

          <div className="border-t border-line py-10 sm:px-8 sm:last:pr-0 lg:border-t-0">
            <ColumnHeading id="footer-contact">Contact</ColumnHeading>
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
