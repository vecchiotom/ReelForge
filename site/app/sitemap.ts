import type { MetadataRoute } from 'next';
import { siteConfig } from '@/lib/site-config';

interface SitemapEntry {
  path: string;
  changeFrequency: MetadataRoute.Sitemap[number]['changeFrequency'];
  priority: number;
}

// Routes that exist today, plus the /legal/* routes Phase 4 adds — listed ahead of time per the
// site rollout plan so this file doesn't need to change again once those pages land. Adding a
// future route is a one-line addition to this array.
const routes: SitemapEntry[] = [
  { path: '/', changeFrequency: 'weekly', priority: 1.0 },
  { path: '/features', changeFrequency: 'monthly', priority: 0.8 },
  { path: '/about', changeFrequency: 'monthly', priority: 0.8 },
  { path: '/contact', changeFrequency: 'monthly', priority: 0.6 },
  // Not yet routable — added by Phase 4. Safe to list now: sitemap entries for pages that
  // don't exist yet are ignored by crawlers, and this avoids a second edit to this file later.
  { path: '/legal/privacy', changeFrequency: 'yearly', priority: 0.3 },
  { path: '/legal/terms', changeFrequency: 'yearly', priority: 0.3 },
];

export default function sitemap(): MetadataRoute.Sitemap {
  return routes.map(({ path, changeFrequency, priority }) => ({
    url: new URL(path, siteConfig.url).toString(),
    changeFrequency,
    priority,
  }));
}
