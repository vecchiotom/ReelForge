import Link from 'next/link';
import { siteConfig } from '@/lib/site-config';

export function SiteHeader() {
  return (
    <header className="border-b border-neutral-200">
      <div className="mx-auto flex max-w-6xl items-center justify-between px-6 py-4">
        <Link href="/" className="text-lg font-semibold text-brand-600">
          {siteConfig.name}
        </Link>
      </div>
    </header>
  );
}
