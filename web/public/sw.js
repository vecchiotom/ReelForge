// Minimal hand-written service worker for the /web dashboard (registered with scope '/app/' —
// see components/shell/ServiceWorkerRegistration.tsx). Deliberately not a library (no Workbox):
// this app's caching needs are narrow enough that a generic "cache everything" strategy would be
// actively wrong here — see the fetch handler below.

const CACHE_NAME = 'reelforge-shell-v1';
const BASE_PATH = '/app';
const OFFLINE_URL = `${BASE_PATH}/offline`;

// Tiny app shell: just enough to render the offline fallback page and its icons/manifest.
const PRECACHE_URLS = [OFFLINE_URL, `${BASE_PATH}/manifest.webmanifest`, `${BASE_PATH}/favicon-192.png`];

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches
      .open(CACHE_NAME)
      .then((cache) => cache.addAll(PRECACHE_URLS))
      .then(() => self.skipWaiting())
  );
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((keys) => Promise.all(keys.filter((key) => key !== CACHE_NAME).map((key) => caches.delete(key))))
      .then(() => self.clients.claim())
  );
});

self.addEventListener('fetch', (event) => {
  const { request } = event;

  // Never touch non-GET requests (POST/PUT/DELETE aren't cacheable and must always hit the
  // network — intercepting them here would silently break writes).
  if (request.method !== 'GET') return;

  const url = new URL(request.url);

  // Never touch API calls. Nginx translates the httpOnly auth cookie into an Authorization
  // header per-request (see CLAUDE.md's auth flow) — caching an authenticated API response in
  // the Cache API would persist it outside that per-request auth check, risking leaking one
  // user's data to the next user of a shared machine.
  if (url.pathname.includes('/api/')) return;

  // Never touch the live SSE progress stream (GET /api/v1/workflows/events, matched above by
  // '/api/' already, but Accept-header-based requests to the same idea are guarded again here
  // defensively). A cached SSE response would be a permanently frozen, misleading progress bar —
  // worse than the network error the browser would otherwise show.
  if (request.headers.get('accept')?.includes('text/event-stream')) return;

  // Cache-first for Next's content-hashed build assets (immutable by construction) and for
  // icons/manifest requests.
  const isNextStaticAsset = url.pathname.startsWith(`${BASE_PATH}/_next/static/`);
  const isIconOrManifest = url.pathname.includes('/icon') || url.pathname.endsWith('manifest.webmanifest');

  if (isNextStaticAsset || isIconOrManifest) {
    event.respondWith(
      caches.match(request).then(
        (cached) =>
          cached ||
          fetch(request).then((response) => {
            const copy = response.clone();
            caches.open(CACHE_NAME).then((cache) => cache.put(request, copy));
            return response;
          })
      )
    );
    return;
  }

  // Page navigations: try the network, fall back to the cached offline page when it's
  // unreachable, instead of the browser's own error page.
  if (request.mode === 'navigate') {
    event.respondWith(fetch(request).catch(() => caches.match(OFFLINE_URL)));
    return;
  }

  // Everything else: let the request hit the network untouched.
});
