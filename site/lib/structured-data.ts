import { siteConfig } from '@/lib/site-config';

// Deliberately no `contactPoint`/`address` here — those need real legal facts that don't exist
// until Phase 4 introduces `legal-placeholders.ts`. Extend this file once that data is real
// rather than inventing placeholder values now.
export function organizationSchema() {
  return {
    '@context': 'https://schema.org',
    '@type': 'Organization',
    name: siteConfig.name,
    url: siteConfig.url,
    logo: `${siteConfig.url}/icon.svg`,
  };
}

export function websiteSchema() {
  return {
    '@context': 'https://schema.org',
    '@type': 'WebSite',
    name: siteConfig.name,
    url: siteConfig.url,
  };
}
