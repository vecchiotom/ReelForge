export const siteConfig = {
  name: 'ReelForge',
  shortDescription: 'Generate promotional videos with AI-driven agentic workflows.',
  url: process.env.NEXT_PUBLIC_SITE_URL ?? 'https://reelforge.com',
  dashboardPath: '/app',
  loginPath: '/app/login',
  nav: [
    { label: 'Features', href: '/features' },
    { label: 'About', href: '/about' },
    { label: 'Contact', href: '/contact' },
  ],
} as const;
