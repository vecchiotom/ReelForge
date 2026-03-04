import { NextResponse } from 'next/server';
import type { NextRequest } from 'next/server';

const publicPaths = ['/login', '/api/'];

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
    return NextResponse.redirect(new URL('/login', request.url));
  }

  let user: { email: string; isAdmin: boolean; mustChangePassword: boolean } | null = null;
  try {
    user = JSON.parse(decodeURIComponent(userCookie));
  } catch {
    return NextResponse.redirect(new URL('/login', request.url));
  }

  // Must change password → force to change-password page
  if (user?.mustChangePassword && pathname !== '/change-password') {
    return NextResponse.redirect(new URL('/change-password', request.url));
  }

  // Admin routes require isAdmin
  if (pathname.startsWith('/admin') && !user?.isAdmin) {
    return NextResponse.redirect(new URL('/dashboard', request.url));
  }

  return NextResponse.next();
}

export const config = {
  matcher: ['/((?!_next/static|_next/image|favicon.ico).*)'],
};
