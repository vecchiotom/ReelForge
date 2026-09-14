type Columns = 2 | 3 | 4;

const COLUMN_CLASS: Record<Columns, string> = {
  2: 'grid-cols-2',
  3: 'grid-cols-3',
  4: 'grid-cols-4',
};

interface GridOverlayProps {
  columns?: Columns;
  horizontalAt?: string[];
  className?: string;
}

/**
 * Decorative hairline grid overlay. The parent element MUST be
 * `relative overflow-hidden` — this component absolutely fills it.
 */
export function GridOverlay({ columns = 4, horizontalAt, className }: GridOverlayProps) {
  const cells = Array.from({ length: columns });

  return (
    <div
      aria-hidden="true"
      className={`pointer-events-none absolute inset-0 ${className ?? ''}`}
    >
      <div className={`grid h-full w-full ${COLUMN_CLASS[columns]}`}>
        {cells.map((_, i) => (
          <div
            key={i}
            className={i < columns - 1 ? 'border-r border-line' : ''}
          />
        ))}
      </div>
      {horizontalAt?.map((top, i) => (
        <div
          key={i}
          className="absolute inset-x-0 border-t border-line"
          style={{ top }}
        />
      ))}
    </div>
  );
}
