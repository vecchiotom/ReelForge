'use client';
import { useEffect, useMemo, useRef, useState } from 'react';
import dynamic from 'next/dynamic';
import { useReducedMotion } from '@/lib/use-reduced-motion';
import { SceneBoundary } from './SceneBoundary';

// Static line-drawing stand-in for reduced-motion users — keeps the
// "viewfinder" panel reading as a technical readout with zero motion and
// zero WebGL, rather than falling back to the hero's gradient-blob treatment.
function ViewfinderStatic() {
  return (
    <svg
      aria-hidden="true"
      viewBox="0 0 100 100"
      className="h-full w-full p-6 text-accent"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.2"
    >
      <path d="M50 8 L88 50 L50 92 L12 50 Z" />
      <path d="M50 32 L69 50 L50 68 L31 50 Z" />
    </svg>
  );
}

// The only component other files should import to get the viewfinder 3D
// scene. Same SSR/lazy-load/visibility architecture as HeroSceneMount, but
// without the scroll-progress ref (this scene only ever spins in place).
export function ViewfinderMount() {
  const reduced = useReducedMotion();
  const wrapRef = useRef<HTMLDivElement>(null);
  const [visible, setVisible] = useState(false);

  const LazyViewfinderScene = useMemo(
    () =>
      dynamic(() => import('./ViewfinderScene'), {
        ssr: false,
        loading: () => <ViewfinderStatic />,
      }),
    [],
  );

  useEffect(() => {
    const wrap = wrapRef.current;
    if (!wrap) return;

    const observer = new IntersectionObserver(
      ([entry]) => setVisible(entry.isIntersecting),
      { threshold: 0 },
    );
    observer.observe(wrap);

    const onVisibilityChange = () => {
      if (document.hidden) setVisible(false);
    };
    document.addEventListener('visibilitychange', onVisibilityChange);

    return () => {
      observer.disconnect();
      document.removeEventListener('visibilitychange', onVisibilityChange);
    };
  }, []);

  if (reduced) {
    return <ViewfinderStatic />;
  }

  return (
    <div ref={wrapRef} aria-hidden="true" className="h-full w-full">
      <SceneBoundary fallback={<ViewfinderStatic />}>
        <LazyViewfinderScene active={visible} />
      </SceneBoundary>
    </div>
  );
}
