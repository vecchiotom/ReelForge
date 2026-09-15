'use client';

import Link from 'next/link';
import { useCookieConsent } from './CookieConsentProvider';
import { buttonClass } from '@/components/ui/button-styles';

export function CookieBanner() {
  const { consent, accept, reject } = useCookieConsent();

  // No choice recorded yet (or the mount effect hasn't run): show the banner. Once a choice has
  // been made (`'granted'` or `'denied'`), render nothing.
  if (consent !== null) {
    return null;
  }

  return (
    <div
      role="dialog"
      aria-modal="false"
      aria-labelledby="cookie-banner-heading"
      className="fixed inset-x-0 bottom-0 z-50 border-t border-line bg-paper pb-[env(safe-area-inset-bottom)] shadow-[0_-4px_16px_rgba(0,0,0,0.08)]"
    >
      <div className="mx-auto flex w-full max-w-6xl flex-col gap-4 px-4 py-4 sm:flex-row sm:items-center sm:justify-between sm:px-6 lg:px-8">
        <p id="cookie-banner-heading" className="font-mono text-sm text-ink-muted">
          We use Google Analytics cookies to understand how visitors use this site — they&apos;re
          only set if you accept. Read our{' '}
          <Link href="/legal/privacy" className="font-medium text-accent-strong hover:text-accent-ink">
            privacy policy
          </Link>{' '}
          for details.
        </p>
        {/* Reject/Accept share identical size classes from buttonClass — only color differs,
            so neither choice is visually weighted over the other. */}
        <div className="flex gap-3 sm:shrink-0">
          <button
            type="button"
            onClick={reject}
            className={`flex-1 sm:flex-none ${buttonClass({ variant: 'outline' })}`}
          >
            Reject
          </button>
          <button
            type="button"
            onClick={accept}
            className={`flex-1 sm:flex-none ${buttonClass({ variant: 'accent' })}`}
          >
            Accept
          </button>
        </div>
      </div>
    </div>
  );
}
