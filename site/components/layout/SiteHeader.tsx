'use client';

import { useState } from 'react';
import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { siteConfig } from '@/lib/site-config';
import { Button } from '@/components/ui/Button';
import { buttonClass } from '@/components/ui/button-styles';

function Lockup() {
  return (
    <Link href="/" className="flex items-center gap-2.5">
      <svg
        width="32"
        height="32"
        viewBox="0 0 28 28"
        fill="none"
        aria-hidden="true"
        focusable="false"
        className="shrink-0"
      >
        <rect width="28" height="28" rx="2" className="fill-accent-strong" />
        <path d="M9 8.5L19 14L9 19.5V8.5Z" fill="white" />
      </svg>
      <span className="flex flex-col leading-none">
        <span className="font-display text-lg font-bold leading-none text-ink">{siteConfig.name}</span>
        <span className="mt-1 font-mono text-[10px] uppercase tracking-eyebrow text-ink-muted">
          {siteConfig.tagline}
        </span>
      </span>
    </Link>
  );
}

export function SiteHeader() {
  const [menuOpen, setMenuOpen] = useState(false);
  const pathname = usePathname();

  return (
    <header className="sticky top-0 z-40 h-20 border-b border-line bg-paper">
      <div className="mx-auto flex h-20 max-w-6xl items-center justify-between px-4 sm:px-6 lg:px-8">
        <Lockup />

        <nav className="hidden md:flex md:items-center md:gap-8">
          {siteConfig.nav.map((item) => {
            const active = pathname === item.href;
            return (
              <Link
                key={item.href}
                href={item.href}
                aria-current={active ? 'page' : undefined}
                className={`relative py-2 font-mono text-xs uppercase tracking-eyebrow transition-colors hover:text-ink ${
                  active ? 'text-ink' : 'text-ink-muted'
                }`}
              >
                {item.label}
                <span
                  aria-hidden="true"
                  className={`absolute inset-x-0 -bottom-px h-px bg-accent transition-opacity ${
                    active ? 'opacity-100' : 'opacity-0'
                  }`}
                />
              </Link>
            );
          })}
        </nav>

        <div className="hidden items-center gap-5 md:flex">
          <a href="/app/login" className="font-mono text-xs uppercase tracking-eyebrow text-ink-muted hover:text-ink">
            Sign in
          </a>
          <Button href="/contact" variant="accent" skew>
            Get in touch
          </Button>
        </div>

        <button
          type="button"
          aria-expanded={menuOpen}
          aria-controls="mobile-nav"
          aria-label={menuOpen ? 'Close menu' : 'Open menu'}
          onClick={() => setMenuOpen((open) => !open)}
          className="flex h-11 w-11 items-center justify-center border border-line text-ink hover:bg-paper-2 md:hidden"
        >
          {menuOpen ? (
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" aria-hidden="true" focusable="false">
              <path d="M6 6L18 18M18 6L6 18" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" />
            </svg>
          ) : (
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" aria-hidden="true" focusable="false">
              <path d="M4 7H20M4 12H20M4 17H20" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" />
            </svg>
          )}
        </button>
      </div>

      {menuOpen && (
        <div
          id="mobile-nav"
          className="absolute inset-x-0 top-20 w-full border-b border-line bg-paper px-4 pb-6 pt-2 shadow-sm md:hidden"
        >
          <nav className="flex flex-col">
            {siteConfig.nav.map((item) => (
              <Link
                key={item.href}
                href={item.href}
                onClick={() => setMenuOpen(false)}
                className="flex min-h-11 items-center border-b border-line font-mono text-sm uppercase tracking-eyebrow text-ink-2"
              >
                {item.label}
              </Link>
            ))}
            <a
              href="/app/login"
              className="flex min-h-11 items-center border-b border-line font-mono text-sm uppercase tracking-eyebrow text-ink-2"
            >
              Sign in
            </a>
            <Link
              href="/contact"
              onClick={() => setMenuOpen(false)}
              className={`mt-4 ${buttonClass({ variant: 'accent', block: true })}`}
            >
              Get in touch
            </Link>
          </nav>
        </div>
      )}
    </header>
  );
}
