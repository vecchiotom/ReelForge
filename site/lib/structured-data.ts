import { siteConfig } from '@/lib/site-config';
import type { Faq } from '@/lib/faqs';

// Deliberately no `contactPoint`/`address` here — those need real legal facts that don't exist
// until `legal-placeholders.ts` is filled in. Extend this file once that data is real rather than
// inventing placeholder values now. Same rule for `aggregateRating`/`offers` on the
// SoftwareApplication below: no ratings and no price are published, because none are real, and
// fabricated review or price markup is a manual-action-level violation, not a growth hack.
export function organizationSchema() {
  return {
    '@context': 'https://schema.org',
    '@type': 'Organization',
    name: siteConfig.name,
    url: siteConfig.url,
    logo: `${siteConfig.url}/icon.svg`,
    description: siteConfig.shortDescription,
  };
}

export function websiteSchema() {
  return {
    '@context': 'https://schema.org',
    '@type': 'WebSite',
    name: siteConfig.name,
    url: siteConfig.url,
    description: siteConfig.shortDescription,
  };
}

export function softwareApplicationSchema() {
  return {
    '@context': 'https://schema.org',
    '@type': 'SoftwareApplication',
    name: siteConfig.name,
    url: siteConfig.url,
    applicationCategory: 'MultimediaApplication',
    applicationSubCategory: 'Video Production Software',
    operatingSystem: 'Web-based',
    description: siteConfig.shortDescription,
    featureList: [
      'AI-scripted promotional video production',
      'Automatic editing of raw product footage',
      'Motion graphics, captions and titles',
      'Colour grading, background music and sound effects',
      'Automated quality review of every draft',
      'Bring your own AI provider and self-hosted deployment',
    ],
  };
}

export function faqSchema(faqs: Faq[]) {
  return {
    '@context': 'https://schema.org',
    '@type': 'FAQPage',
    mainEntity: faqs.map((faq) => ({
      '@type': 'Question',
      name: faq.question,
      acceptedAnswer: {
        '@type': 'Answer',
        text: faq.answer,
      },
    })),
  };
}

// `position` is 1-based and must be contiguous, so the caller passes the trail in display order
// and never has to hand-maintain the indices.
export function breadcrumbSchema(trail: { name: string; path: string }[]) {
  return {
    '@context': 'https://schema.org',
    '@type': 'BreadcrumbList',
    itemListElement: trail.map((crumb, index) => ({
      '@type': 'ListItem',
      position: index + 1,
      name: crumb.name,
      item: new URL(crumb.path, siteConfig.url).toString(),
    })),
  };
}
