export const siteConfig = {
  name: 'ReelForge',
  shortDescription: 'Generate promotional videos with AI-driven agentic workflows.',
  url: process.env.NEXT_PUBLIC_SITE_URL ?? 'https://reelforge.com',
  dashboardPath: '/app',
  loginPath: '/app/login',
} as const;
