import { siteConfig } from '@/lib/site-config';

export function SiteFooter() {
  return (
    <footer className="border-t border-neutral-200">
      <div className="mx-auto max-w-6xl px-6 py-6 text-sm text-neutral-500">
        &copy; {new Date().getFullYear()} {siteConfig.name}
      </div>
    </footer>
  );
}
