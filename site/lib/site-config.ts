export const siteConfig = {
  name: 'ReelForge',
  tagline: 'AI video team',
  shortDescription:
    'ReelForge is an AI video team that turns your product into finished promo videos — scripted, edited, and reviewed by AI agents, ready to publish.',
  url: process.env.NEXT_PUBLIC_SITE_URL ?? 'https://reelforge.com',
  dashboardPath: '/app',
  loginPath: '/app/login',
  nav: [
    { label: 'How it works', href: '/#how-it-works' },
    { label: 'Features', href: '/features' },
    { label: 'About', href: '/about' },
    { label: 'Contact', href: '/contact' },
  ],
} as const;
