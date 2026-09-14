'use client';

import { useCookieConsent } from './CookieConsentProvider';

export function CookiePreferencesLink({ className }: { className?: string }) {
  const { reopen } = useCookieConsent();

  return (
    <button
      type="button"
      onClick={reopen}
      className={`text-sm text-neutral-600 underline-offset-2 hover:text-neutral-900 hover:underline ${className ?? ''}`.trim()}
    >
      Cookie preferences
    </button>
  );
}
