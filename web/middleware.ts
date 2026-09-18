import { NextResponse } from 'next/server';
import type { NextRequest } from 'next/server';

const publicPaths = [
  '/login',
  '/api/',
  // PWA assets: must be reachable without auth. The manifest's <link> tag is emitted on every
  // page including /login, and the service worker precaches /offline at install time — both need
  // to resolve to real content rather than a 307 to /login for the auth-gating to not silently
  // break installability/precaching.
  '/offline',
  '/sw.js',
  '/manifest.webmanifest',
  '/icon.svg',
  '/apple-icon.png',
  '/favicon-192.png',
  '/favicon-512.png',
  '/favicon-512-maskable.png',
];

export function middleware(request: NextRequest) {
  const { pathname } = request.nextUrl;

  // Allow public paths
  if (publicPaths.some((p) => pathname.startsWith(p))) {
    return NextResponse.next();
  }

  const userCookie = request.cookies.get('reelforge_user')?.value;
  const tokenCookie = request.cookies.get('reelforge_token')?.value;

  // No auth → redirect to login
  if (!tokenCookie || !userCookie) {
    // request.nextUrl.clone() preserves the app's basePath ('/app') when
    // re-serialized; building a URL from a bare path plus request.url instead
    // would silently drop it.
    const url = request.nextUrl.clone();
    url.pathname = '/login';
    return NextResponse.redirect(url);
  }

  let user: { email: string; isAdmin: boolean; mustChangePassword: boolean } | null = null;
  try {
    user = JSON.parse(decodeURIComponent(userCookie));
  } catch {
    const url = request.nextUrl.clone();
    url.pathname = '/login';
    return NextResponse.redirect(url);
  }

  // Must change password → force to change-password page
  if (user?.mustChangePassword && pathname !== '/change-password') {
    const url = request.nextUrl.clone();
    url.pathname = '/change-password';
    return NextResponse.redirect(url);
  }

  // Admin routes require isAdmin
  if (pathname.startsWith('/admin') && !user?.isAdmin) {
    const url = request.nextUrl.clone();
    url.pathname = '/dashboard';
    return NextResponse.redirect(url);
  }

  return NextResponse.next();
}

export const config = {
  matcher: ['/((?!_next/static|_next/image|favicon.ico).*)'],
};
