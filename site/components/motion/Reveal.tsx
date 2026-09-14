'use client';
import { createElement, useEffect, useRef, useState, type ElementType, type ReactNode } from 'react';
import { useReducedMotion } from '@/lib/use-reduced-motion';

export function Reveal({
  children,
  delay = 0,
  as: Tag = 'div',
  className = '',
}: {
  children: ReactNode;
  delay?: number;
  as?: ElementType;
  className?: string;
}) {
  const reduced = useReducedMotion();
  const ref = useRef<HTMLElement>(null);
  const [visible, setVisible] = useState(false);

  useEffect(() => {
    if (reduced) return; // reduced-motion users see content immediately, no observer needed
    const el = ref.current;
    if (!el) return;
    const io = new IntersectionObserver(
      ([entry]) => {
        if (entry.isIntersecting) {
          setVisible(true);
          io.unobserve(el); // one-shot reveal, never re-hide on scroll-up
        }
      },
      { threshold: 0.12, rootMargin: '0px 0px -8% 0px' },
    );
    io.observe(el);
    return () => io.disconnect();
  }, [reduced]);

  if (reduced) {
    // No wrapper styling at all — fully visible immediately, zero risk of stuck-hidden content
    return createElement(Tag, { className }, children);
  }

  return createElement(
    Tag,
    {
      ref,
      className: `transition-[opacity,transform] duration-500 ease-out ${visible ? 'opacity-100 translate-y-0' : 'opacity-0 translate-y-4'} ${className}`,
      style: { transitionDelay: `${delay}ms` },
    },
    children,
  );
}
