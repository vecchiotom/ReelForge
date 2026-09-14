// Plain Server Component — pure CSS, no client runtime needed. Serves three roles:
// the next/dynamic loading placeholder, the reduced-motion replacement, and the
// WebGL-failure fallback (see SceneBoundary). Occupies the exact box the real
// canvas will occupy so swapping between the two causes zero layout shift.
export function HeroFallback() {
  return (
    <div
      aria-hidden="true"
      className="relative aspect-square w-full max-w-[420px] overflow-hidden rounded-[2px] border border-line bg-paper-2 bg-[radial-gradient(circle_at_35%_30%,var(--color-accent)_0%,transparent_55%),radial-gradient(circle_at_65%_70%,var(--color-accent-deep)_0%,transparent_60%)]"
    />
  );
}
