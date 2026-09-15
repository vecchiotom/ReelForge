'use client';

import { useRef, useState, useEffect } from 'react';
import { Eyebrow } from '@/components/ui/Eyebrow';
import { useReducedMotion } from '@/lib/use-reduced-motion';

// Genuine, documented parts of the ReelForge stack (see CLAUDE.md) — text
// wordmarks, never logo images, since ReelForge has no real partners or
// customers to showcase and no third-party trademark assets are used.
const TECHNOLOGIES = ['Remotion', 'ffmpeg', 'PostgreSQL', 'Docker', 'Next.js', 'Azure OpenAI'];

export function TechStrip() {
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
        <Eyebrow>Runs on</Eyebrow>
        <div className="flex items-center gap-2">
          <button
            type="button"
            aria-label="Scroll technologies left"
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
            aria-label="Scroll technologies right"
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
        {TECHNOLOGIES.map((tech, i) => (
          <div
            key={tech}
            className={`flex shrink-0 snap-start items-center border-l border-line px-8 py-4 ${
              i === TECHNOLOGIES.length - 1 ? 'border-r' : ''
            }`}
          >
            <span className="whitespace-nowrap font-display text-lg font-bold uppercase text-ink-2">{tech}</span>
          </div>
        ))}
      </div>
    </div>
  );
}
