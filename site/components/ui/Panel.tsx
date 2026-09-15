import type { ReactNode } from 'react';
import { CornerBrackets } from './CornerBrackets';

type Tone = 'paper' | 'paper-2' | 'ink' | 'accent-deep';

interface PanelProps {
  tone?: Tone;
  brackets?: boolean;
  className?: string;
  children: ReactNode;
}

const TONE_CLASS: Record<Tone, string> = {
  paper: 'bg-paper',
  'paper-2': 'bg-paper-2',
  ink: 'bg-ink text-white border-ink',
  'accent-deep': 'bg-accent-deep text-white border-accent-deep',
};

export function Panel({ tone = 'paper', brackets, className, children }: PanelProps) {
  return (
    <div className={`relative border border-line ${TONE_CLASS[tone]} ${className ?? ''}`}>
      {brackets && <CornerBrackets tone={tone === 'ink' || tone === 'accent-deep' ? 'white' : 'ink'} />}
      {children}
    </div>
  );
}
