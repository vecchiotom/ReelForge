'use client';

import { useState } from 'react';
import Link from 'next/link';
import { siteConfig } from '@/lib/site-config';

function Wordmark() {
  return (
    <Link href="/" className="flex items-center gap-2 text-lg font-semibold text-neutral-900">
      <svg
        width="28"
        height="28"
        viewBox="0 0 28 28"
        fill="none"
        aria-hidden="true"
        focusable="false"
        className="shrink-0"
      >
        <rect width="28" height="28" rx="7" className="fill-brand-600" />
        <path d="M9 8.5L19 14L9 19.5V8.5Z" fill="white" />
      </svg>
      <span>{siteConfig.name}</span>
    </Link>
  );
}

export function SiteHeader() {
  const [menuOpen, setMenuOpen] = useState(false);

  return (
    <header className="sticky top-0 z-40 h-16 border-b border-neutral-200 bg-white/90 backdrop-blur supports-[backdrop-filter]:bg-white/75">
      <div className="mx-auto flex h-16 max-w-6xl items-center justify-between px-4 sm:px-6 lg:px-8">
        <Wordmark />

        <nav className="hidden md:flex md:items-center md:gap-8">
          {siteConfig.nav.map((item) => (
            <Link
              key={item.href}
              href={item.href}
              className="text-sm font-medium text-neutral-600 hover:text-neutral-900"
            >
              {item.label}
            </Link>
          ))}
        </nav>

        <div className="hidden items-center gap-4 md:flex">
          <a
            href="/app/login"
            className="text-sm font-medium text-neutral-600 hover:text-neutral-900"
          >
            Sign in
          </a>
          <Link
            href="/contact"
            className="inline-flex min-h-11 items-center justify-center rounded-md bg-brand-600 px-4 py-2 text-sm font-semibold text-white hover:bg-brand-700"
          >
            Get in touch
          </Link>
        </div>

        <button
          type="button"
          aria-expanded={menuOpen}
          aria-controls="mobile-nav"
          aria-label={menuOpen ? 'Close menu' : 'Open menu'}
          onClick={() => setMenuOpen((open) => !open)}
          className="flex h-11 w-11 items-center justify-center rounded-md text-neutral-700 hover:bg-neutral-100 md:hidden"
        >
          {menuOpen ? (
            <svg width="22" height="22" viewBox="0 0 24 24" fill="none" aria-hidden="true" focusable="false">
              <path
                d="M6 6L18 18M18 6L6 18"
                stroke="currentColor"
                strokeWidth="2"
                strokeLinecap="round"
              />
            </svg>
          ) : (
            <svg width="22" height="22" viewBox="0 0 24 24" fill="none" aria-hidden="true" focusable="false">
              <path
                d="M4 7H20M4 12H20M4 17H20"
                stroke="currentColor"
                strokeWidth="2"
                strokeLinecap="round"
              />
            </svg>
          )}
        </button>
      </div>

      {menuOpen && (
        <div
          id="mobile-nav"
          className="absolute inset-x-0 top-16 w-full border-b border-neutral-200 bg-white px-4 pb-6 pt-2 shadow-sm md:hidden"
        >
          <nav className="flex flex-col">
            {siteConfig.nav.map((item) => (
              <Link
                key={item.href}
                href={item.href}
                onClick={() => setMenuOpen(false)}
                className="flex min-h-11 items-center border-b border-neutral-100 text-base font-medium text-neutral-700"
              >
                {item.label}
              </Link>
            ))}
            <a
              href="/app/login"
              className="flex min-h-11 items-center border-b border-neutral-100 text-base font-medium text-neutral-700"
            >
              Sign in
            </a>
            <Link
              href="/contact"
              onClick={() => setMenuOpen(false)}
              className="mt-4 inline-flex min-h-11 items-center justify-center rounded-md bg-brand-600 px-4 py-2 text-base font-semibold text-white"
            >
              Get in touch
            </Link>
          </nav>
        </div>
      )}
    </header>
  );
}
