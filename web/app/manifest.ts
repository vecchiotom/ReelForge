import type { MetadataRoute } from 'next';

// PWA manifest for the /web dashboard only — /site (the public marketing site) deliberately does
// not ship one. Both apps share a single origin (see CLAUDE.md's Nginx Reverse Proxy table), and
// a service worker registered from /site would claim scope '/', the entire origin including
// /app/*. Registering everything under this app's basePath keeps the worker's scope to '/app/'.
export default function manifest(): MetadataRoute.Manifest {
  return {
    name: 'ReelForge',
    short_name: 'ReelForge',
    description: 'AI-powered promotional video generation',
    // Must include the /app basePath (web/next.config.ts) — without it, the installed app would
    // launch straight into the marketing site at '/' instead of the dashboard.
    start_url: '/app/dashboard',
    scope: '/app/',
    id: '/app/',
    display: 'standalone',
    // Matches web/app/theme.ts's primaryColor: 'violet' (Mantine's violet-6 swatch), and the same
    // hex the marketing site already uses for its own manifest (site/app/manifest.ts).
    theme_color: '#7C3AED',
    // layout.tsx sets defaultColorScheme="auto", so there's no single canonical background from
    // the theme. Judgment call: this dashboard is a technical/internal tool, so we use Mantine's
    // dark-mode surface color (dark-7, its default body background in dark mode) as the PWA
    // splash-screen background rather than white — closer to what a returning user actually sees.
    background_color: '#1a1b1e',
    icons: [
      { src: 'favicon-192.png', sizes: '192x192', type: 'image/png', purpose: 'any' },
      { src: 'favicon-512.png', sizes: '512x512', type: 'image/png', purpose: 'any' },
      { src: 'favicon-512-maskable.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
    ],
  };
}
