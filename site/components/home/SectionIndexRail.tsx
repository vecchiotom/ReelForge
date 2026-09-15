const SECTIONS = [
  { href: '#how-it-works', label: '01', name: 'How it works' },
  { href: '#whats-inside', label: '02', name: "What's inside" },
  { href: '#get-started', label: '03', name: 'Get started' },
];

// Wayfinding rail, large screens only — a nice-to-have, not essential content.
export function SectionIndexRail() {
  return (
    <div className="absolute right-5 top-1/2 hidden -translate-y-1/2 flex-col gap-3 lg:flex">
      {SECTIONS.map((section) => (
        <a
          key={section.href}
          href={section.href}
          aria-label={`Jump to ${section.name}`}
          className="flex h-11 w-11 items-center justify-center border border-line bg-paper font-mono text-[10px] text-ink-muted transition-colors hover:border-accent hover:text-ink"
        >
          {section.label}
        </a>
      ))}
    </div>
  );
}
