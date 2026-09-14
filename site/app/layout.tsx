import type { Metadata } from 'next';
import { Suspense } from 'react';
import { SiteHeader } from '@/components/layout/SiteHeader';
import { SiteFooter } from '@/components/layout/SiteFooter';
import { StickyMobileCta } from '@/components/layout/StickyMobileCta';
import { JsonLd } from '@/components/seo/JsonLd';
import { CookieConsentProvider } from '@/components/consent/CookieConsentProvider';
import { CookieBanner } from '@/components/consent/CookieBanner';
import { GoogleAnalytics } from '@/components/analytics/GoogleAnalytics';
import { PageViewTracker } from '@/components/analytics/PageViewTracker';
import { siteConfig } from '@/lib/site-config';
import { organizationSchema, websiteSchema } from '@/lib/structured-data';
import './globals.css';

export const metadata: Metadata = {
  metadataBase: new URL(siteConfig.url),
  title: {
    default: siteConfig.name,
    template: `%s | ${siteConfig.name}`,
  },
  description: siteConfig.shortDescription,
  openGraph: {
    type: 'website',
    url: siteConfig.url,
    siteName: siteConfig.name,
    title: siteConfig.name,
    description: siteConfig.shortDescription,
    locale: 'en_US',
  },
  twitter: {
    card: 'summary_large_image',
    title: siteConfig.name,
    description: siteConfig.shortDescription,
  },
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  // Only ever sourced from the env var — never hardcode a measurement ID.
  // `GoogleAnalytics` independently re-checks cookie consent before it
  // renders anything, so this only controls whether the *possibility* of
  // loading GA exists at all for this deployment.
  const gaId = process.env.NEXT_PUBLIC_GA_MEASUREMENT_ID;

  return (
    <html lang="en">
      <body className="font-sans antialiased min-h-screen flex flex-col bg-white text-neutral-900">
        <a
          href="#main"
          className="sr-only focus:not-sr-only focus:fixed focus:left-4 focus:top-4 focus:z-50 focus:rounded-md focus:bg-brand-600 focus:px-4 focus:py-2 focus:text-sm focus:font-semibold focus:text-white"
        >
          Skip to content
        </a>
        <CookieConsentProvider>
          {gaId && (
            <GoogleAnalytics gaId={gaId}>
              <Suspense fallback={null}>
                <PageViewTracker />
              </Suspense>
            </GoogleAnalytics>
          )}
          <SiteHeader />
          <main id="main" className="flex-1 pb-20 md:pb-0">
            {children}
          </main>
          <SiteFooter />
          <StickyMobileCta />
          <CookieBanner />
        </CookieConsentProvider>
        <JsonLd data={organizationSchema()} />
        <JsonLd data={websiteSchema()} />
      </body>
    </html>
  );
}
