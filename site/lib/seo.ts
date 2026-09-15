import type { Metadata } from 'next';
import { siteConfig } from '@/lib/site-config';

export function buildMetadata({
  title,
  description,
  path,
  noIndex,
}: {
  title: string;
  description: string;
  path: string;
  noIndex?: boolean;
}): Metadata {
  const url = new URL(path, siteConfig.url).toString();

  // `title.absolute` bypasses ancestor `title.template` merging entirely. We need that here
  // rather than passing `title` as a plain string, because Next.js does not apply a layout's
  // title.template to a page.tsx that shares the same route segment as that layout — which is
  // exactly the case for the root `app/page.tsx` (verified against the built output: nested
  // routes like /about get the " | ReelForge" suffix from the template automatically, but the
  // root page never does). Composing the full title explicitly here keeps every page's <title>
  // consistent regardless of route depth, instead of splitting the behavior between
  // template-inheritance (nested routes) and manual concatenation (root route).
  const fullTitle = `${title} | ${siteConfig.name}`;

  const metadata: Metadata = {
    title: { absolute: fullTitle },
    description,
    alternates: {
      canonical: url,
    },
    openGraph: {
      type: 'website',
      url,
      siteName: siteConfig.name,
      title,
      description,
      locale: 'en_US',
    },
    twitter: {
      card: 'summary_large_image',
      title,
      description,
    },
  };

  if (noIndex) {
    metadata.robots = { index: false, follow: false };
  }

  return metadata;
}
