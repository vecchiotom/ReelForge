# Site Design System

The public marketing site (`/site`) went through a 5-phase visual redesign to a bold, technical
"schematic" aesthetic — purple/white/near-black, two display/mono webfonts, decorative hairline
primitives, and two genuine WebGL 3D scenes on the homepage hero. This doc is the reference for
that system: the exact tokens, why they were chosen, and the rules that keep the site's
accessibility/performance invariants (see `docs/marketing-site.md` and
`docs/checklist-traceability.md`) intact as it evolves. It does not repeat the SEO/legal/consent
material those two docs already own.

## Palette

All tokens live in `site/app/globals.css`'s `@theme` block. There is no other place a color is
defined for use in Tailwind utility classes — a `bg-accent` or `text-ink-muted` class always
resolves back to one of these.

| Token | Hex | Role |
|---|---|---|
| `--color-paper` | `#FFFFFF` | Page background |
| `--color-paper-2` | `#F7F6F9` | Secondary/alternating section background |
| `--color-paper-3` | `#EFEDF2` | Tertiary surface (subtle recess) |
| `--color-ink` | `#0B0B12` | Primary text, near-black (not pure black) |
| `--color-ink-2` | `#3F3D4A` | Secondary heading/emphasis text |
| `--color-ink-muted` | `#55525F` | Body copy on paper backgrounds |
| `--color-ink-faint` | `#8C8899` | De-emphasized text / decorative glyph color |
| `--color-line` | `#E4E1EA` | Default hairline border |
| `--color-line-strong` | `#8C8899` | Emphasized hairline (table rules, dividers that need to read) |
| `--color-accent` | `#A855F7` | Primary purple accent |
| `--color-accent-hover` | `#B77DF8` | Hover state for accent surfaces |
| `--color-accent-strong` | `#7C3AED` | Accent on higher-contrast surfaces (buttons, focus rings) |
| `--color-accent-ink` | `#6D28D9` | Accent used as text color on white |
| `--color-accent-deep` | `#2E1065` | Darkest accent tint, used as a panel background |
| `--color-accent-tint` | `#F5F1FE` | Lightest accent tint, used as a subtle fill |
| `--color-danger` | `#B91C1C` | Error text |
| `--color-danger-border` | `#DC2626` | Error input border |
| `--color-danger-tint` | `#FEF2F2` | Error banner/background fill |

Phase 3 of the redesign removed the old `--color-brand-*` shim entirely — there is no legacy alias
left pointing at these values under a different name. Confirm this before adding a new token:
`grep -n "color-brand" site/app/globals.css` should return nothing.

### Why these exact hex values — measured contrast

Every text/background and interactive-border pairing actually used in the UI was checked against
WCAG 2 contrast requirements before being adopted. These are the pairings that matter; re-run a
contrast check before changing any of the hexes above, since a "close enough" purple can silently
drop a pairing below its required ratio.

| Foreground | Background | Ratio | Requirement met |
|---|---|---|---|
| `#0B0B12` (ink) | `#A855F7` (accent) | 4.96:1 | AA, normal text |
| `#FFFFFF` | `#7C3AED` (accent-strong) | 5.70:1 | AA, normal text |
| `#6D28D9` (accent-ink) | `#FFFFFF` (paper) | 7.10:1 | AAA, normal text |
| `#55525F` (ink-muted) | `#FFFFFF` / `#F7F6F9` | 7.61 / 7.07:1 | AAA, normal text |
| `#8C8899` (ink-faint / line-strong) | `#FFFFFF` / `#F7F6F9` | 3.44 / 3.20:1 | ≥3:1 only — used for interactive control **borders** (WCAG 1.4.11 non-text contrast), **never** for text |
| `#B91C1C` (danger) | `#FFFFFF` | 6.47:1 | AAA, normal text |
| `#FFFFFF` / `#C4B5FD` | `#0B0B12` (ink) | 19.61 / 10.62:1 | AAA, normal text |

The one deliberately sub-AA-for-text pairing (`ink-faint`/`line-strong` at ~3.2–3.4:1) is used only
where WCAG's non-text contrast rule (1.4.11, ≥3:1) applies — hairline borders around inputs and
other interactive controls — and is never used to color actual text. If a future change starts
using `ink-faint` for a text color, re-run a contrast check against whatever background it lands
on; at ~3.2:1 it will fail normal-text AA (needs 4.5:1).

## Typography

Two Google Fonts, loaded via `next/font/google` in `site/app/layout.tsx`:

- **`--font-display`** (Chakra Petch) — headings, CTAs, eyebrow numerals, anything meant to read as
  bold/technical signage.
- **`--font-mono`** (JetBrains Mono) — eyebrow labels, nav items, captions, form labels, timestamps
  — the "readout" register of the design.
- **`--font-sans`** — the original zero-download system font stack, kept (not replaced) for
  long-form prose: legal pages and feature/about body copy. Long paragraphs set in a display or
  mono face hurt readability at body-copy sizes and line lengths; system-sans is the more legible
  choice for a multi-paragraph privacy policy or terms page. This is a one-line-reversible decision
  (swap the `font-sans` utility for `font-mono`/`font-display` on those pages) if the site owner
  later wants the technical faces used everywhere, including prose.

## Decorative primitive catalogue

All in `site/components/ui/`:

| Primitive | Purpose |
|---|---|
| `GridOverlay` | Absolute-fill hairline column/row grid — the schematic backdrop behind hero/section content. |
| `RegistrationMark` | Small solid accent square, print-registration-mark styling, placed at panel corners. |
| `CornerBrackets` | Four L-shaped corner brackets around a panel, viewfinder/technical-readout styling. |
| `ChromeWidget` | Small bordered glyph box (close/target/plus/grid icon) — desktop-chrome-style ornament. |
| `Eyebrow` | Small-caps mono label with a leading accent dot, used above headings. |
| `Button` | The site's one button/link component — every CTA, everywhere, goes through this. |
| `Panel` | Bordered content container with optional tone and corner brackets — the base "card" surface. |

### The aria-hidden / non-interactive contract

`GridOverlay`, `RegistrationMark`, `CornerBrackets`, and `ChromeWidget` are **purely decorative** —
each renders `aria-hidden="true"` on its root element, carries no `tabIndex`, no `onClick`, and no
interactive ARIA role, and this must never regress. A screen reader and a keyboard-only user should
be able to ignore all four completely; a design change that adds a click handler or a real link
inside one of these components breaks that contract and must move the interactive affordance out
to a real, focusable element instead (usually a `Button` or a plain `<a>`).

`Eyebrow` is the one partial exception worth calling out: the label *text* is real, meaningful
content (not `aria-hidden`) — only its small leading accent square is a decorative
`aria-hidden="true"` dot. `Button` is intentionally interactive (it's the CTA primitive, not
decoration). `Panel` is a plain layout container with no ARIA semantics of its own; it only
becomes non-interactive-by-construction because nothing inside it is added except through other,
already-compliant primitives.

Re-verify the contract with `grep -rn "tabIndex\|onClick" site/components/ui/GridOverlay.tsx
site/components/ui/RegistrationMark.tsx site/components/ui/CornerBrackets.tsx
site/components/ui/ChromeWidget.tsx` — it should return nothing.

## The 3D strategy — honest, not fabricated

The hero's 3D objects (a faceted purple icosahedron core, a wireframe cage, and an orbit ring; a
wireframe/solid octahedron pair in the smaller "viewfinder" panel) are **genuine, live-rendered
WebGL geometry** — not a fabricated product photo, not a mascot render, not a stock 3D asset
passed off as bespoke. There is no real 3D asset for this product to render, and none was faked to
look like one; abstract generative geometry, rendered honestly as what it is, was the design
choice that fit the technical/schematic aesthetic without inventing a visual fact about the
product. This mirrors the site's broader "no invented facts" discipline (see
`docs/marketing-site.md`) applied to visuals rather than copy.

## SSR isolation rule

`three` and `@react-three/fiber` are imported **only** in two files:

- `site/components/three/HeroScene.tsx`
- `site/components/three/ViewfinderScene.tsx`

Every other file reaches the scenes only through their mount wrappers —
`site/components/three/HeroSceneMount.tsx` and `site/components/three/ViewfinderMount.tsx` — which
load the real scene component via `next/dynamic(() => import('./HeroScene'), { ssr: false })` (and
the `ViewfinderScene` equivalent). This is required, not stylistic: Next.js prerenders/statically
generates this site's pages on the server, and WebGL has no server-side implementation — a
top-level `import * as THREE from 'three'` reached during SSR either no-ops uselessly or throws,
depending on what it touches (`document`/`window`/canvas context). `next/dynamic(..., { ssr: false
})` is also only legal inside a Client Component, which is why the mount files are `'use client'`
separately from the scene files and from whatever page renders the mount.

Verify the isolation holds with:

```bash
grep -rln "from 'three'\|from '@react-three/fiber'" site/app site/components site/lib
```

This should list exactly `HeroScene.tsx` and `ViewfinderScene.tsx` and nothing else.

## Reduced-motion rule

`site/lib/use-reduced-motion.ts` exports `useReducedMotion()`, which **defaults to `true`** (assume
reduced motion) until a `matchMedia('(prefers-reduced-motion: reduce)')` check proves otherwise on
mount. Every motion-bearing piece of the site is built on top of this one hook, and all of them
must keep failing safe toward "do nothing that moves":

- **`HeroSceneMount` / `ViewfinderMount`** (`site/components/three/`) — when `reduced` is true, the
  component returns its static fallback (`HeroFallback`, a pure-CSS gradient box; `ViewfinderStatic`,
  a static SVG) **without ever calling `next/dynamic`'s import** — the three.js/`@react-three/fiber`
  chunk is not requested at all, not even prefetched, for a reduced-motion visitor.
- **`Reveal`** (`site/components/motion/Reveal.tsx`) — when `reduced` is true, it renders children
  immediately, fully visible, with no wrapper transition classes and no `IntersectionObserver` — a
  true no-op, not just a fast animation.
- **`useScrollParallax`** (`site/lib/use-scroll-parallax.ts`) — when `reduced` is true, the effect
  returns immediately; no scroll/resize listeners are attached and the element's transform is never
  touched.

Re-confirm this hasn't regressed by reading all five files together
(`lib/use-reduced-motion.ts`, `components/three/HeroSceneMount.tsx`,
`components/three/ViewfinderMount.tsx`, `components/motion/Reveal.tsx`,
`lib/use-scroll-parallax.ts`) — each must branch on the same hook and each must choose "render the
static/no-op path, don't just animate faster" as its reduced-motion behavior.

## Bundle code-split contract

- Only `HeroScene.tsx` and `ViewfinderScene.tsx` may import `three` or `@react-three/fiber`
  (see "SSR isolation rule" above — the same rule doubles as the bundle boundary).
- Only `HeroSceneMount.tsx` and `ViewfinderMount.tsx` may import those two scene files, and only
  via `next/dynamic(..., { ssr: false })` — never a static `import` of `HeroScene`/`ViewfinderScene`
  from anywhere else, which would pull the three.js chunk back into a shared/eagerly-loaded bundle.
- `site/components/home/Hero.tsx` (the only page that renders the mounts) imports the *mount*
  components, never the scene components directly.

Net effect, confirmed at every phase of the redesign and re-confirmed for this phase's regression
pass: the three.js/`@react-three/fiber` code (~130KB+ gzipped) ships in its own lazy chunk, is
requested only when the homepage's hero mounts on a client that does *not* have reduced motion set,
and every non-homepage route's First Load JS stays within about 1KB of the pre-redesign baseline.
Current `npm run build` route table (this phase's regression pass): homepage First Load JS is
112KB, every other route is 103–107KB, and the three.js chunk does not appear in any route's
First Load JS figure at all (it's a route-level lazy chunk fetched at runtime, not part of the
initial bundle Next.js's build output attributes to a route).

## The `@react-three/fiber@9.4.0` pin

`package.json` pins `@react-three/fiber` to exactly `9.4.0` — no caret — because versions ≥9.5.0
declare a peer dependency of `react: ">=19 <19.3"`, which excludes this project's installed React
`19.3.0`. That would break `npm ci` (used by the production Dockerfile's `deps` stage, and by this
phase's full regression rebuild). `9.4.0` declares `react: "^19.0.0"`, which resolves cleanly
against React 19.3.0. Do not bump `@react-three/fiber` past `9.4.0` without first confirming its
peer-dependency range against whatever React version is installed at the time.

`three` itself is declared with a caret (`^0.186.0`) and currently resolves to `0.186.0` — it has
no equivalent peer-dependency conflict with React, so it wasn't pinned exactly.

No `@react-three/drei` and no `framer-motion` were added anywhere in the redesign: the 3D scenes
are built from three.js core geometries/materials only, and all scroll-driven motion (`Reveal`,
`useScrollParallax`) is a small hand-rolled `IntersectionObserver`/CSS-transition implementation
(~1KB), not a motion library.

## No invented facts, applied to visuals

- The reference design's "partner logos" strip became `site/components/home/UseCaseStrip.tsx`, a
  "Made for" strip of the video jobs ReelForge produces ("Product launches", "Feature demos",
  "Sales outreach", …) rendered as plain text — never as logos, and never captioned as "partners"
  or "customers". It previously listed stack technologies under a "Runs on" heading; that was
  accurate but spoke to engineers rather than buyers, so the slot now carries use cases (which
  also earn their keep as keyword coverage). The no-invented-logos rule is unchanged.
- The reference design's social-icon rail became `site/components/home/SectionIndexRail.tsx`, real
  in-page section-anchor links (`#how-it-works`, `#whats-inside`, `#get-started`) — no fabricated
  social-media URLs were added anywhere on the site.

## How to extend this system

- Reuse `Reveal`, `GridOverlay`, `Eyebrow`, `Button`, and `Panel` for new sections rather than
  hand-rolling new decorative markup or a new button/card implementation. If a new decorative shape
  doesn't fit the existing primitives, add a new one to `site/components/ui/` following the same
  `aria-hidden`/non-interactive contract described above, rather than inlining `aria-hidden` divs
  ad hoc across pages.
- New hardcoded hex colors in `.tsx` files are not allowed outside `opengraph-image.tsx`,
  `twitter-image.tsx`, and `components/three/*` — those three exceptions exist because `next/og`'s
  `ImageResponse` and three.js material colors aren't CSS and can't reference the `@theme` tokens.
  Everywhere else, use the Tailwind utility classes backed by the tokens in the Palette section
  above (`bg-accent`, `text-ink-muted`, etc.), so a future palette change stays a one-file edit.
  Verify with:
  ```bash
  grep -rln "#[0-9a-fA-F]\{6\}" site/app site/components --include='*.tsx' \
    | grep -v opengraph-image | grep -v twitter-image | grep -v components/three
  ```
  which should return nothing.
- Before changing any palette hex, re-run a contrast check for every pairing in the "Why these
  exact hex values" table above — a value swapped for a "close enough" purple can silently drop a
  pairing below the WCAG ratio it currently clears.
