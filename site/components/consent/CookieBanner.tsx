'use client';

import Link from 'next/link';
import { useCookieConsent } from './CookieConsentProvider';

const buttonClass =
  'inline-flex min-h-11 flex-1 items-center justify-center rounded-md border px-4 py-2 text-sm font-semibold sm:flex-none';

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
      className="fixed inset-x-0 bottom-0 z-50 border-t border-neutral-200 bg-white pb-[env(safe-area-inset-bottom)] shadow-[0_-4px_16px_rgba(0,0,0,0.08)]"
    >
      <div className="mx-auto flex w-full max-w-6xl flex-col gap-4 px-4 py-4 sm:flex-row sm:items-center sm:justify-between sm:px-6 lg:px-8">
        <p id="cookie-banner-heading" className="text-sm text-neutral-600">
          We use Google Analytics cookies to understand how visitors use this site — they&apos;re
          only set if you accept. Read our{' '}
          <Link href="/legal/privacy" className="font-medium text-brand-600 hover:text-brand-700">
            privacy policy
          </Link>{' '}
          for details.
        </p>
        <div className="flex gap-3 sm:shrink-0">
          <button
            type="button"
            onClick={reject}
            className={`${buttonClass} border-neutral-300 bg-white text-neutral-900 hover:bg-neutral-50`}
          >
            Reject
          </button>
          <button
            type="button"
            onClick={accept}
            className={`${buttonClass} border-brand-600 bg-brand-600 text-white hover:bg-brand-700`}
          >
            Accept
          </button>
        </div>
      </div>
    </div>
  );
}
