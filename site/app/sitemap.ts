import type { MetadataRoute } from 'next';
import { siteConfig } from '@/lib/site-config';

interface SitemapEntry {
  path: string;
  changeFrequency: MetadataRoute.Sitemap[number]['changeFrequency'];
  priority: number;
}

// Only routes that are actually live belong here — a submitted sitemap that repeatedly 404s is a
// negative quality signal to crawlers, so don't pre-list a page ahead of the phase that ships it.
const routes: SitemapEntry[] = [
  { path: '/', changeFrequency: 'weekly', priority: 1.0 },
  { path: '/features', changeFrequency: 'monthly', priority: 0.9 },
  { path: '/about', changeFrequency: 'monthly', priority: 0.7 },
  { path: '/contact', changeFrequency: 'monthly', priority: 0.8 },
  { path: '/legal/privacy', changeFrequency: 'yearly', priority: 0.2 },
  { path: '/legal/terms', changeFrequency: 'yearly', priority: 0.2 },
];

export default function sitemap(): MetadataRoute.Sitemap {
  // One timestamp for the whole generation rather than per-route dates: every route here is
  // statically compiled into the same build, so a build is exactly when any of them could last
  // have changed. Inventing distinct per-page dates would be fiction, and crawlers discount
  // `lastmod` values they catch being wrong.
  const lastModified = new Date();

  return routes.map(({ path, changeFrequency, priority }) => ({
    url: new URL(path, siteConfig.url).toString(),
    lastModified,
    changeFrequency,
    priority,
  }));
}
