'use client';

import { useEffect } from 'react';

// Registers the dashboard's service worker (public/sw.js, served at /app/sw.js) with an explicit
// '/app/' scope. /web and /site share one origin (see CLAUDE.md's Nginx Reverse Proxy table), so
// scoping matters: registering from anywhere under the /app basePath keeps the worker from ever
// claiming the marketing site's routes.
export function ServiceWorkerRegistration() {
  useEffect(() => {
    if (!('serviceWorker' in navigator)) return;

    navigator.serviceWorker.register('/app/sw.js', { scope: '/app/' }).catch((error) => {
      console.error('Service worker registration failed:', error);
    });
  }, []);

  return null;
}
