import type { ReactNode } from 'react';

interface EyebrowProps {
  children: ReactNode;
  as?: 'p' | 'span' | 'div';
  className?: string;
}

export function Eyebrow({ children, as: Tag = 'p', className }: EyebrowProps) {
  return (
    <Tag
      className={`flex items-center gap-2 font-mono text-[11px] uppercase tracking-eyebrow text-ink-muted ${className ?? ''}`}
    >
      <span aria-hidden="true" className="h-2 w-2 shrink-0 bg-accent" />
      {children}
    </Tag>
  );
}
