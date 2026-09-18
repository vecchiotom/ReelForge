'use client';

import { useRef, useState, useEffect } from 'react';
import { Eyebrow } from '@/components/ui/Eyebrow';
import { useReducedMotion } from '@/lib/use-reduced-motion';

// The video jobs ReelForge is actually built to produce. Deliberately plain
// text, never customer logos or partner marks — ReelForge has no real
// customers to showcase, and inventing a logo wall would be a false claim.
const USE_CASES = [
  'Product launches',
  'Feature demos',
  'Sales outreach',
  'Paid social ads',
  'Customer onboarding',
  'Release highlights',
  'Investor updates',
];

export function UseCaseStrip() {
  const reduced = useReducedMotion();
  const stripRef = useRef<HTMLDivElement>(null);
  const [atStart, setAtStart] = useState(true);
  const [atEnd, setAtEnd] = useState(false);

  const updateEdges = () => {
    const el = stripRef.current;
    if (!el) return;
    setAtStart(el.scrollLeft <= 0);
    setAtEnd(el.scrollLeft + el.clientWidth >= el.scrollWidth - 1);
  };

  useEffect(() => {
    updateEdges();
  }, []);

  const scrollBy = (delta: number) => {
    stripRef.current?.scrollBy({ left: delta, behavior: reduced ? 'auto' : 'smooth' });
  };

  return (
    <div className="border-y border-line py-8">
      <div className="flex items-center justify-between">
        <Eyebrow>Made for</Eyebrow>
        <div className="flex items-center gap-2">
          <button
            type="button"
            aria-label="Scroll use cases left"
            disabled={atStart}
            onClick={() => scrollBy(-240)}
            className="flex h-11 w-11 items-center justify-center border border-line text-ink-muted transition-colors hover:text-ink disabled:cursor-not-allowed disabled:opacity-30"
          >
            <svg width="14" height="14" viewBox="0 0 14 14" fill="none" aria-hidden="true">
              <path d="M9 2L4 7L9 12" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
            </svg>
          </button>
          <button
            type="button"
            aria-label="Scroll use cases right"
            disabled={atEnd}
            onClick={() => scrollBy(240)}
            className="flex h-11 w-11 items-center justify-center border border-line text-ink-muted transition-colors hover:text-ink disabled:cursor-not-allowed disabled:opacity-30"
          >
            <svg width="14" height="14" viewBox="0 0 14 14" fill="none" aria-hidden="true">
              <path d="M5 2L10 7L5 12" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
            </svg>
          </button>
        </div>
      </div>

      <div
        ref={stripRef}
        onScroll={updateEdges}
        className="mt-6 flex snap-x overflow-x-auto [scrollbar-width:none] [&::-webkit-scrollbar]:hidden"
      >
        {USE_CASES.map((useCase, i) => (
          <div
            key={useCase}
            className={`flex shrink-0 snap-start items-center border-l border-line px-8 py-4 ${
              i === USE_CASES.length - 1 ? 'border-r' : ''
            }`}
          >
            <span className="whitespace-nowrap font-display text-lg font-bold uppercase text-ink-2">{useCase}</span>
          </div>
        ))}
      </div>
    </div>
  );
}
