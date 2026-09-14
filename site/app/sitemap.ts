import type { MetadataRoute } from 'next';
import { siteConfig } from '@/lib/site-config';

interface SitemapEntry {
  path: string;
  changeFrequency: MetadataRoute.Sitemap[number]['changeFrequency'];
  priority: number;
}

// Only routes that are actually live belong here — a submitted sitemap that repeatedly 404s is a
// negative quality signal to crawlers, so don't pre-list a page ahead of the phase that ships it.
// /legal/privacy and /legal/terms are added here once Phase 4 creates those routes.
const routes: SitemapEntry[] = [
  { path: '/', changeFrequency: 'weekly', priority: 1.0 },
  { path: '/features', changeFrequency: 'monthly', priority: 0.8 },
  { path: '/about', changeFrequency: 'monthly', priority: 0.8 },
  { path: '/contact', changeFrequency: 'monthly', priority: 0.6 },
];

export default function sitemap(): MetadataRoute.Sitemap {
  return routes.map(({ path, changeFrequency, priority }) => ({
    url: new URL(path, siteConfig.url).toString(),
    changeFrequency,
    priority,
  }));
}
