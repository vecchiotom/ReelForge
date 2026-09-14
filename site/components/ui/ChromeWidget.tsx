type Glyph = 'close' | 'target' | 'plus' | 'grid';

interface ChromeWidgetProps {
  glyph: Glyph;
  className?: string;
}

function GlyphPath({ glyph }: { glyph: Glyph }) {
  switch (glyph) {
    case 'close':
      return (
        <>
          <path d="M2 2 L12 12" strokeWidth="1.5" strokeLinecap="round" />
          <path d="M12 2 L2 12" strokeWidth="1.5" strokeLinecap="round" />
        </>
      );
    case 'target':
      return (
        <>
          <circle cx="7" cy="7" r="5" strokeWidth="1.5" />
          <circle cx="7" cy="7" r="1" strokeWidth="1.5" fill="currentColor" />
        </>
      );
    case 'plus':
      return (
        <>
          <path d="M7 2 L7 12" strokeWidth="1.5" strokeLinecap="round" />
          <path d="M2 7 L12 7" strokeWidth="1.5" strokeLinecap="round" />
        </>
      );
    case 'grid':
      return (
        <>
          <rect x="2" y="2" width="4" height="4" strokeWidth="1.5" />
          <rect x="8" y="2" width="4" height="4" strokeWidth="1.5" />
          <rect x="2" y="8" width="4" height="4" strokeWidth="1.5" />
          <rect x="8" y="8" width="4" height="4" strokeWidth="1.5" />
        </>
      );
  }
}

/** Purely decorative chrome ornament — renders a `<div>`, never interactive/focusable. */
export function ChromeWidget({ glyph, className }: ChromeWidgetProps) {
  return (
    <div
      aria-hidden="true"
      className={`h-8 w-8 border border-line flex items-center justify-center text-ink-faint ${className ?? ''}`}
    >
      <svg aria-hidden="true" width="14" height="14" viewBox="0 0 14 14" stroke="currentColor" fill="none">
        <GlyphPath glyph={glyph} />
      </svg>
    </div>
  );
}
