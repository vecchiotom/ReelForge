'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { useCookieConsent } from '@/components/consent/CookieConsentProvider';

const HIDDEN_PATHS = ['/contact', '/contact/thank-you'];

export function StickyMobileCta() {
  const pathname = usePathname();
  const { consent } = useCookieConsent();

  if (HIDDEN_PATHS.includes(pathname)) {
    return null;
  }

  // The cookie banner is also fixed to the bottom of the viewport while no choice has been made
  // yet (`consent === null`) — don't stack a second fixed bar on top of it.
  if (consent === null) {
    return null;
  }

  return (
    <div className="fixed inset-x-0 bottom-0 z-50 border-t border-neutral-200 bg-white pb-[env(safe-area-inset-bottom)] md:hidden">
      <div className="px-4 py-3">
        <Link
          href="/contact"
          className="flex min-h-11 w-full items-center justify-center rounded-md bg-brand-600 px-4 py-3 text-sm font-semibold text-white"
        >
          Get in touch
        </Link>
      </div>
    </div>
  );
}
