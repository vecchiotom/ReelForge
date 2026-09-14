import { Container } from '@/components/layout/Container';
import { Skeleton } from '@/components/ui/Skeleton';

// Route-level loading fallback shown by Next.js while a page's data/render is
// in flight. Mirrors the header height + Container/Section rhythm of real
// pages so the loading→content swap isn't jarring.
export default function Loading() {
  return (
    <div role="status" aria-live="polite">
      <span className="sr-only">Loading…</span>

      {/* Header-height spacer (SiteHeader is h-20). */}
      <div className="h-20" aria-hidden="true" />

      <div className="py-16 sm:py-20 lg:py-28">
        <Container>
          <Skeleton className="mx-auto h-8 w-2/3 max-w-md" />
          <Skeleton className="mx-auto mt-4 h-4 w-full max-w-xl" />

          <div className="mt-10 grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-3">
            <Skeleton className="h-40 w-full" />
            <Skeleton className="h-40 w-full" />
            <Skeleton className="h-40 w-full" />
          </div>
        </Container>
      </div>
    </div>
  );
}
