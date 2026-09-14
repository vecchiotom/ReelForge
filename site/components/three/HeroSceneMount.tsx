'use client';
import { useEffect, useMemo, useRef, useState } from 'react';
import dynamic from 'next/dynamic';
import { useReducedMotion } from '@/lib/use-reduced-motion';
import { HeroFallback } from './HeroFallback';
import { SceneBoundary } from './SceneBoundary';

function clamp(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value));
}

// The ONLY component other files should import to get the hero 3D scene.
// Isolates every WebGL-scene concern (SSR exclusion, lazy loading,
// reduced-motion opt-out, visibility gating) behind a plain,
// server-renderable surface.
export function HeroSceneMount() {
  const reduced = useReducedMotion();
  const wrapRef = useRef<HTMLDivElement>(null);
  const progressRef = useRef(0);
  const [visible, setVisible] = useState(false);

  // `next/dynamic` with `{ ssr: false }` is only legal inside a Client
  // Component in the App Router — this is why this file is 'use client'
  // separately from HeroScene.tsx and from whatever page renders this mount.
  const LazyHeroScene = useMemo(
    () =>
      dynamic(() => import('./HeroScene'), {
        ssr: false,
        loading: () => <HeroFallback />,
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

    let rafId: number | null = null;
    const updateProgress = () => {
      rafId = null;
      const rect = wrap.getBoundingClientRect();
      const denom = window.innerHeight + rect.height;
      progressRef.current = clamp(
        (window.innerHeight - rect.top) / (denom || 1),
        0,
        1,
      );
    };
    const onScrollOrResize = () => {
      if (rafId !== null) return;
      rafId = requestAnimationFrame(updateProgress);
    };
    const onVisibilityChange = () => {
      if (document.hidden) {
        setVisible(false);
      } else {
        updateProgress();
        observer.takeRecords();
      }
    };

    updateProgress();
    window.addEventListener('scroll', onScrollOrResize, { passive: true });
    window.addEventListener('resize', onScrollOrResize, { passive: true });
    document.addEventListener('visibilitychange', onVisibilityChange);

    return () => {
      observer.disconnect();
      window.removeEventListener('scroll', onScrollOrResize);
      window.removeEventListener('resize', onScrollOrResize);
      document.removeEventListener('visibilitychange', onVisibilityChange);
      if (rafId !== null) cancelAnimationFrame(rafId);
    };
  }, []);

  // Reduced-motion users never trigger the dynamic import — the three.js
  // chunk never begins downloading for them at all.
  if (reduced) {
    return <HeroFallback />;
  }

  return (
    <div ref={wrapRef} aria-hidden="true" className="relative aspect-square w-full max-w-[420px]">
      <SceneBoundary fallback={<HeroFallback />}>
        <LazyHeroScene progressRef={progressRef} active={visible} />
      </SceneBoundary>
    </div>
  );
}
