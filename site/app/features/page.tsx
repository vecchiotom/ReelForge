import type { ReactNode } from 'react';
import { Container } from '@/components/layout/Container';
import { Section } from '@/components/layout/Section';
import { Eyebrow } from '@/components/ui/Eyebrow';
import { Button } from '@/components/ui/Button';
import { Reveal } from '@/components/motion/Reveal';
import { buildMetadata } from '@/lib/seo';

export const metadata = buildMetadata({
  title: 'Features',
  description:
    'Explore the agentic workflow engine, full agent library, Remotion rendering, ffmpeg-based video derushing, and review-loop scoring that power ReelForge.',
  path: '/features',
});

function AccentList({ items }: { items: ReactNode[] }) {
  return (
    <ul className="space-y-2">
      {items.map((item, i) => (
        <li key={i} className="flex items-start gap-3">
          <span aria-hidden="true" className="mt-1.5 h-2 w-2 shrink-0 bg-accent" />
          <span>{item}</span>
        </li>
      ))}
    </ul>
  );
}

export default function FeaturesPage() {
  return (
    <>
      <div className="border-b border-line pt-16 pb-10 sm:pt-20 lg:pt-24">
        <Container>
          <Eyebrow>Under the hood</Eyebrow>
          <h1 className="mt-4 text-balance font-display text-[clamp(2rem,5vw,3.5rem)] font-bold uppercase leading-[0.95] tracking-[-0.02em] text-ink">
            Features
          </h1>
          <p className="mt-4 max-w-2xl font-mono text-sm leading-relaxed text-ink-muted">
            The agentic workflow engine, the agent library, and the video pipeline it drives.
          </p>
        </Container>
      </div>

      <Reveal as="div">
        <Section className="border-b border-line">
          <Eyebrow>Orchestration</Eyebrow>
          <h2 className="mt-3 font-display text-2xl font-bold uppercase tracking-[-0.01em] text-ink">
            Agentic workflow engine
          </h2>
          <div className="mt-4 max-w-3xl space-y-4 text-ink-muted">
            <p>
              Every video is produced by a workflow: a sequence of steps that runs agents, evaluates
              conditions, loops, and can fan work out in parallel. Workflows are built visually,
              step by step, and executed by a dedicated engine that consumes execution requests off
              a message queue.
            </p>
            <p>
              Step types cover more than plain agent calls: conditionals branch on prior output,
              for-each steps repeat over a list, and review loops send low-scoring output back
              through production automatically.
            </p>
            <AccentList
              items={[
                'Visual, drag-and-drop workflow builder',
                'Conditional branches, loops, and parallel steps',
                'Deterministic extract/transform steps alongside LLM agent steps',
              ]}
            />
          </div>
        </Section>
      </Reveal>

      <Reveal as="div">
        <Section className="border-b border-line bg-paper-2">
          <Eyebrow>Agents</Eyebrow>
          <h2 className="mt-3 font-display text-2xl font-bold uppercase tracking-[-0.01em] text-ink">
            Agent library
          </h2>
          <div className="mt-4 max-w-3xl space-y-4 text-ink-muted">
            <p>
              Agents are grouped by what they do in the pipeline, from understanding your codebase
              to producing the final script and video plan.
            </p>
            <AccentList
              items={[
                'Analysis agents inventory code structure, dependencies, components, routes, and styling and theming from your project.',
                'Translation agents map analyzed structure and an animation strategy onto Remotion components.',
                'Production agents direct, script, and author the actual video, plus editors that plan story cuts and motion graphics.',
                'Quality agents review and score each draft against the brief.',
              ]}
            />
          </div>
        </Section>
      </Reveal>

      <Reveal as="div">
        <Section className="border-b border-line">
          <Eyebrow>Rendering</Eyebrow>
          <h2 className="mt-3 font-display text-2xl font-bold uppercase tracking-[-0.01em] text-ink">
            Remotion-based rendering
          </h2>
          <div className="mt-4 max-w-3xl space-y-4 text-ink-muted">
            <p>
              Videos aren&apos;t templated slideshows. They&apos;re React components rendered
              frame-by-frame with Remotion, driven by the plan your production agents assemble.
            </p>
            <AccentList
              items={[
                'Programmatic composition, not fixed templates',
                "Animation strategy derived from your project's own style and theming",
              ]}
            />
          </div>
        </Section>
      </Reveal>

      <Reveal as="div">
        <Section className="border-b border-line bg-paper-2">
          <Eyebrow>Video editing</Eyebrow>
          <h2 className="mt-3 font-display text-2xl font-bold uppercase tracking-[-0.01em] text-ink">
            Source-video derushing and motion graphics
          </h2>
          <div className="mt-4 max-w-3xl space-y-4 text-ink-muted">
            <p>
              When you bring your own raw footage, ffmpeg-based analysis detects silence and shot
              boundaries, optionally transcribes the audio, and produces a bounded, id-anchored view
              of the footage for an editorial agent to work from. It decides what to keep, never a
              raw timestamp.
            </p>
            <p>
              A separate compile step resolves those decisions back to frame-accurate cuts and can
              layer in motion-graphics overlays (lower-thirds, titles, and callouts) planned by a
              dedicated agent and placed only at deterministic, pre-offered positions.
            </p>
            <AccentList
              items={[
                'Silence and shot detection via ffmpeg',
                'Optional transcription for spoken content',
                'Motion-graphics overlays composited during the final encode',
              ]}
            />
          </div>
        </Section>
      </Reveal>

      <Reveal as="div">
        <Section className="border-b border-line">
          <Eyebrow>Quality</Eyebrow>
          <h2 className="mt-3 font-display text-2xl font-bold uppercase tracking-[-0.01em] text-ink">
            Review-loop quality scoring
          </h2>
          <div className="mt-4 max-w-3xl space-y-4 text-ink-muted">
            <p>
              A review agent scores each production draft. If the score falls short of the
              workflow&apos;s threshold, the pipeline loops back through production automatically
              instead of shipping a weak result.
            </p>
          </div>
        </Section>
      </Reveal>

      <Reveal as="div">
        <Section className="bg-paper-2">
          <Eyebrow>Inference</Eyebrow>
          <h2 className="mt-3 font-display text-2xl font-bold uppercase tracking-[-0.01em] text-ink">
            Bring your own inference provider
          </h2>
          <div className="mt-4 max-w-3xl space-y-4 text-ink-muted">
            <p>
              ReelForge doesn&apos;t lock you into one model vendor. Configure Azure OpenAI or any
              OpenAI-compatible endpoint per agent, or set a default provider for chat, transcription,
              and vision workloads independently.
            </p>
            <AccentList
              items={[
                'Azure OpenAI or OpenAI-compatible endpoints',
                'Per-agent provider overrides',
                'Self-hostable end to end via Docker Compose',
              ]}
            />
          </div>
        </Section>
      </Reveal>

      <div className="bg-ink text-white">
        <Container>
          <div className="flex flex-col items-center gap-6 border-x border-white/10 px-6 py-16 text-center sm:px-12">
            <h2 className="font-display text-2xl font-bold uppercase tracking-[-0.01em] sm:text-3xl">
              Want to see this on your own project?
            </h2>
            <Button href="/contact" variant="accent" size="lg" skew>
              Get in touch
            </Button>
          </div>
        </Container>
      </div>
    </>
  );
}
