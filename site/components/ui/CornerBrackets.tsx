type Tone = 'ink' | 'white' | 'accent';
type Size = 'sm' | 'md';
type Inset = 'edge' | 'inset';

interface CornerBracketsProps {
  tone?: Tone;
  size?: Size;
  inset?: Inset;
  className?: string;
}

const TONE_CLASS: Record<Tone, string> = {
  ink: 'border-ink',
  white: 'border-white',
  accent: 'border-accent',
};

const SIZE_PX: Record<Size, string> = {
  sm: 'h-2 w-2',
  md: 'h-3 w-3',
};

const OFFSET: Record<Inset, string> = {
  edge: '0',
  inset: '0.5rem',
};

/**
 * Four L-shaped corner brackets. The parent element MUST be `relative` —
 * this component positions each bracket absolutely against it.
 */
export function CornerBrackets({ tone = 'ink', size = 'md', inset = 'edge', className }: CornerBracketsProps) {
  const borderTone = TONE_CLASS[tone];
  const dim = SIZE_PX[size];
  const offset = OFFSET[inset];

  return (
    <div aria-hidden="true" className={`pointer-events-none absolute inset-0 ${className ?? ''}`}>
      <div
        className={`absolute border-t border-l ${borderTone} ${dim}`}
        style={{ top: offset, left: offset }}
      />
      <div
        className={`absolute border-t border-r ${borderTone} ${dim}`}
        style={{ top: offset, right: offset }}
      />
      <div
        className={`absolute border-b border-l ${borderTone} ${dim}`}
        style={{ bottom: offset, left: offset }}
      />
      <div
        className={`absolute border-b border-r ${borderTone} ${dim}`}
        style={{ bottom: offset, right: offset }}
      />
    </div>
  );
}
