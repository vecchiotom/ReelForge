'use client';
import { Container } from '@/components/layout/Container';
import { GridOverlay } from '@/components/ui/GridOverlay';
import { ChromeWidget } from '@/components/ui/ChromeWidget';
import { RegistrationMark } from '@/components/ui/RegistrationMark';
import { Eyebrow } from '@/components/ui/Eyebrow';
import { Button } from '@/components/ui/Button';
import { Panel } from '@/components/ui/Panel';
import { HeroSceneMount } from '@/components/three/HeroSceneMount';
import { ViewfinderMount } from '@/components/three/ViewfinderMount';
import { SectionIndexRail } from './SectionIndexRail';
import { useScrollParallax } from '@/lib/use-scroll-parallax';

export function Hero() {
  // Wrapping div (rather than forwardRef on GridOverlay) keeps the shared
  // primitive untouched for its other call sites (Section.tsx, /contact).
  const gridParallaxRef = useScrollParallax<HTMLDivElement>(10);

  return (
    <section className="relative isolate overflow-hidden border-b border-line bg-paper-2">
      <div ref={gridParallaxRef} className="absolute inset-0">
        <GridOverlay columns={4} horizontalAt={['62%']} />
      </div>
      <ChromeWidget glyph="close" className="absolute left-4 top-4 hidden sm:flex" />
      <ChromeWidget glyph="target" className="absolute right-4 top-4 hidden sm:flex" />
      <RegistrationMark className="absolute left-2 top-2" />
      <RegistrationMark className="absolute right-2 top-2" />
      <RegistrationMark className="absolute bottom-2 left-2" />
      <RegistrationMark className="absolute bottom-2 right-2" />
      <SectionIndexRail />

      <Container>
        <div className="pt-16 pb-6 lg:pt-24">
          <h1 className="text-balance font-display text-[clamp(2.25rem,6.2vw,5.25rem)] font-bold uppercase leading-[0.95] tracking-[-0.02em] text-ink">
            AI agents that turn your codebase into promotional videos
          </h1>
          <Eyebrow className="mt-6">Agentic video production &middot; self-hostable</Eyebrow>
        </div>

        <div className="border-t border-line" aria-hidden="true" />

        <div className="grid gap-10 py-12 lg:grid-cols-12">
          <div className="lg:col-span-4 lg:col-start-1">
            <p className="font-mono text-sm leading-relaxed text-ink-muted">
              Agents analyze your project, script and direct a promo video, and Remotion renders it
              — with a review loop that scores and refines every draft.
            </p>
            <div className="mt-8 flex flex-col gap-3 sm:flex-row sm:items-center">
              <Button href="/contact" variant="accent" size="lg" skew>
                Get in touch
              </Button>
              <Button href="/app/login" variant="outline">
                Sign in
              </Button>
            </div>
          </div>

          <div className="lg:col-span-5 lg:col-start-5">
            <HeroSceneMount />
          </div>

          <div className="lg:col-span-3 lg:col-start-10">
            <Panel tone="ink" brackets className="aspect-square">
              <Eyebrow className="p-3 text-white/70">Render preview</Eyebrow>
              <div className="h-[calc(100%-2.5rem)] w-full">
                <ViewfinderMount />
              </div>
            </Panel>
          </div>
        </div>
      </Container>
    </section>
  );
}
