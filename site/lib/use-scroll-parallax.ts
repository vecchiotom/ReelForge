'use client';
import { useEffect, useRef } from 'react';
import { useReducedMotion } from './use-reduced-motion'; // reads prefers-reduced-motion via the shared hook, no duplicated matchMedia logic here

export function useScrollParallax<T extends HTMLElement>(maxOffsetPx = 10) {
  const ref = useRef<T>(null);
  const reduced = useReducedMotion();

  useEffect(() => {
    if (reduced) return; // no-op entirely under reduced motion
    const el = ref.current;
    if (!el) return;
    let ticking = false;
    const update = () => {
      const rect = el.getBoundingClientRect();
      const vh = window.innerHeight || 1;
      const progress = Math.max(-1, Math.min(1, (rect.top + rect.height / 2 - vh / 2) / vh));
      el.style.transform = `translate3d(0, ${(-progress * maxOffsetPx).toFixed(2)}px, 0)`;
      ticking = false;
    };
    const onScroll = () => {
      if (!ticking) {
        ticking = true;
        requestAnimationFrame(update);
      }
    };
    update();
    window.addEventListener('scroll', onScroll, { passive: true });
    window.addEventListener('resize', onScroll, { passive: true });
    return () => {
      window.removeEventListener('scroll', onScroll);
      window.removeEventListener('resize', onScroll);
    };
  }, [reduced, maxOffsetPx]);

  return ref;
}
