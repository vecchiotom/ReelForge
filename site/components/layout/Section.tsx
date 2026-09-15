import type { ReactNode } from 'react';
import { Container } from './Container';
import { GridOverlay } from '@/components/ui/GridOverlay';

type Tone = 'paper' | 'paper-2' | 'ink';

const TONE_CLASS: Record<Tone, string> = {
  paper: 'bg-paper text-ink',
  'paper-2': 'bg-paper-2 text-ink',
  ink: 'bg-ink text-white',
};

export function Section({
  id,
  className,
  tone,
  grid,
  bordered,
  children,
}: {
  id?: string;
  className?: string;
  /** Optional background tone. Omit to render with no background class, as before. */
  tone?: Tone;
  /** Optional decorative hairline grid overlay behind the section content. */
  grid?: boolean;
  /** Optional hairline top/bottom borders. */
  bordered?: boolean;
  children: ReactNode;
}) {
  const toneClass = tone ? TONE_CLASS[tone] : '';
  const borderClass = bordered ? 'border-y border-line' : '';
  const needsRelative = grid;

  return (
    <section
      id={id}
      className={`py-16 sm:py-20 lg:py-28 ${toneClass} ${borderClass} ${needsRelative ? 'relative isolate overflow-hidden' : ''} ${className ?? ''}`.trim()}
    >
      {grid && <GridOverlay />}
      <Container>{children}</Container>
    </section>
  );
}
