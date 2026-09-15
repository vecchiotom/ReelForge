'use client';

import { usePathname, useSearchParams } from 'next/navigation';
import { useEffect, useRef } from 'react';
import { trackEvent } from '@/lib/gtag';

// NOTE: `useSearchParams()` opts this component out of static rendering and
// requires a <Suspense> boundary around it wherever it's rendered, or
// `next build` fails with "should be wrapped in a suspense boundary" — see
// site/app/layout.tsx.
export function PageViewTracker() {
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const isFirstRun = useRef(true);

  useEffect(() => {
    // Skip the very first run: GA's own `gtag('config', ..., { send_page_view: true })`
    // call (in GoogleAnalytics.tsx) already sends the initial page_view.
    if (isFirstRun.current) {
      isFirstRun.current = false;
      return;
    }

    const query = searchParams.toString();
    trackEvent('page_view', {
      page_path: query ? `${pathname}?${query}` : pathname,
    });
  }, [pathname, searchParams]);

  return null;
}
