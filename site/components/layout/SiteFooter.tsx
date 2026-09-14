import Link from 'next/link';
import { siteConfig } from '@/lib/site-config';

export function SiteFooter() {
  return (
    <footer className="border-t border-neutral-200 bg-neutral-50">
      <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6 lg:px-8">
        <div className="grid grid-cols-1 gap-10 sm:grid-cols-2 lg:grid-cols-4">
          <div>
            <h2 className="text-sm font-semibold text-neutral-900">Product</h2>
            <ul className="mt-4 space-y-3">
              <li>
                <Link href="/features" className="text-sm text-neutral-600 hover:text-neutral-900">
                  Features
                </Link>
              </li>
            </ul>
          </div>

          <div>
            <h2 className="text-sm font-semibold text-neutral-900">Company</h2>
            <ul className="mt-4 space-y-3">
              <li>
                <Link href="/about" className="text-sm text-neutral-600 hover:text-neutral-900">
                  About
                </Link>
              </li>
              <li>
                <Link href="/contact" className="text-sm text-neutral-600 hover:text-neutral-900">
                  Contact
                </Link>
              </li>
            </ul>
          </div>

          <div>
            <h2 className="text-sm font-semibold text-neutral-900">Legal</h2>
            <ul className="mt-4 space-y-3">
              <li>
                <Link
                  href="/legal/privacy"
                  className="text-sm text-neutral-600 hover:text-neutral-900"
                >
                  Privacy policy
                </Link>
              </li>
              <li>
                <Link
                  href="/legal/terms"
                  className="text-sm text-neutral-600 hover:text-neutral-900"
                >
                  Terms of service
                </Link>
              </li>
            </ul>
          </div>

          <div>
            <h2 className="text-sm font-semibold text-neutral-900">Contact</h2>
            {/*
              Minimal/structural for now. Phase 4 replaces the mailto placeholder and
              adds the real postal address using the bracketed tokens defined in
              site/lib/legal-placeholders.ts.
            */}
            <address className="not-italic mt-4 space-y-3 text-sm text-neutral-600">
              <p>
                <a href="mailto:hello@example.com" className="hover:text-neutral-900">
                  hello@example.com
                </a>
              </p>
            </address>
          </div>
        </div>

        <div className="mt-12 border-t border-neutral-200 pt-6 text-sm text-neutral-500">
          &copy; {new Date().getFullYear()} {siteConfig.name}. All rights reserved.
        </div>
      </div>
    </footer>
  );
}
