export function Skeleton({ className }: { className?: string }) {
  return (
    <div
      className={`animate-pulse rounded-none bg-paper-3 ${className ?? ''}`.trim()}
    />
  );
}
