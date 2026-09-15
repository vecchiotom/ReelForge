'use client';

import Script from 'next/script';
import type { ReactNode } from 'react';
import { useCookieConsent } from '@/components/consent/CookieConsentProvider';

export function GoogleAnalytics({ gaId, children }: { gaId: string; children?: ReactNode }) {
  const { consent } = useCookieConsent();

  // Gate independently of whether `gaId` was supplied by the caller: no GA
  // script tag, no `gtag` call, no GA cookie until consent is explicitly
  // 'granted'. `consent` defaults to `null` (no choice yet) and is only ever
  // 'granted' after the visitor accepts the cookie banner.
  if (consent !== 'granted') {
    return children ?? null;
  }

  return (
    <>
      <Script src={`https://www.googletagmanager.com/gtag/js?id=${gaId}`} strategy="afterInteractive" />
      <Script id="ga4-init" strategy="afterInteractive">
        {`
          window.dataLayer = window.dataLayer || [];
          function gtag(){window.dataLayer.push(arguments);}
          window.gtag = gtag;
          gtag('js', new Date());
          gtag('config', '${gaId}', { send_page_view: true });
        `}
      </Script>
      {children}
    </>
  );
}
