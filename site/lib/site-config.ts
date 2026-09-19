export const siteConfig = {
  name: 'ReelForge',
  tagline: 'AI video team',
  shortDescription:
    'ReelForge is an AI video team that turns your product into finished promo videos — scripted, edited, and reviewed by AI agents, ready to publish.',
  // `||`, not `??`: the Docker build declares `ARG NEXT_PUBLIC_SITE_URL` with no default and then
  // `ENV NEXT_PUBLIC_SITE_URL=$NEXT_PUBLIC_SITE_URL`, so a build that passes no build-arg sets this
  // to the EMPTY STRING rather than leaving it unset. `??` only falls back on null/undefined, so an
  // empty value flowed straight through to `new URL(siteConfig.url)` in app/layout.tsx's
  // metadataBase and failed the production build with ERR_INVALID_URL. Every other consumer of this
  // variable already treats empty as absent — .env.example documents leaving it blank.
  url: process.env.NEXT_PUBLIC_SITE_URL || 'https://reelforge.com',
  dashboardPath: '/app',
  loginPath: '/app/login',
  nav: [
    { label: 'How it works', href: '/#how-it-works' },
    { label: 'Features', href: '/features' },
    { label: 'About', href: '/about' },
    { label: 'Contact', href: '/contact' },
  ],
} as const;
