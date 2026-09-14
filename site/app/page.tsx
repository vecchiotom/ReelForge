import { Section } from '@/components/layout/Section';
import { Panel } from '@/components/ui/Panel';
import { Eyebrow } from '@/components/ui/Eyebrow';
import { Button } from '@/components/ui/Button';
import { GridOverlay } from '@/components/ui/GridOverlay';
import { Hero } from '@/components/home/Hero';
import { TechStrip } from '@/components/home/TechStrip';
import { Reveal } from '@/components/motion/Reveal';
import { buildMetadata } from '@/lib/seo';

export const metadata = buildMetadata({
  title: 'AI Agents for Promotional Video Generation',
  description:
    'ReelForge turns your codebase into a promotional video with agentic workflows, Remotion rendering, and a review loop that scores and refines every draft.',
  path: '/',
});

const steps = [
  {
    title: 'Connect your project',
    description: 'Point ReelForge at your codebase and project assets so agents have real context to work from.',
  },
  {
    title: 'Agents analyze your code',
    description:
      'Analysis agents inventory components, routes, dependencies, and styling, then translation agents map it all to Remotion.',
  },
  {
    title: 'Remotion renders the video',
    description: 'Production agents script, direct, and author a promotional video, rendered frame-by-frame with Remotion.',
  },
  {
    title: 'Review, score, and iterate',
    description: 'A review agent scores each draft and loops the pipeline back through production until quality holds.',
  },
];

const features = [
  {
    title: 'Agentic workflow engine',
    description: 'A visual, step-based workflow engine orchestrates every agent run, with conditionals, loops, and parallel steps.',
    icon: (
      <path
        d="M4 6h16M4 12h16M4 18h7"
        stroke="currentColor"
        strokeWidth="1.75"
        strokeLinecap="round"
      />
    ),
  },
  {
    title: 'A full agent library',
    description: 'Analysis, translation, production, and quality agents each specialize in one part of turning code into video.',
    icon: (
      <path
        d="M12 3l8 4v6c0 4.5-3.4 7.7-8 8-4.6-.3-8-3.5-8-8V7l8-4Z"
        stroke="currentColor"
        strokeWidth="1.75"
        strokeLinejoin="round"
      />
    ),
  },
  {
    title: 'Remotion-based rendering',
    description: 'Videos are composed and rendered with Remotion, so every frame is generated programmatically from your content.',
    icon: (
      <path
        d="M5 5h14v14H5z M9 9l7 3.5L9 16V9Z"
        stroke="currentColor"
        strokeWidth="1.75"
        strokeLinejoin="round"
      />
    ),
  },
  {
    title: 'AI-assisted source-video derushing',
    description:
      'ffmpeg-based silence and shot detection, optional transcription, and motion-graphics overlays help clean up raw footage automatically.',
    icon: (
      <path
        d="M4 7h16v10H4z M8 7v10 M16 7v10"
        stroke="currentColor"
        strokeWidth="1.75"
        strokeLinecap="round"
      />
    ),
  },
  {
    title: 'Review-loop quality scoring',
    description: 'A dedicated review agent scores every draft and routes low scores back through production for another pass.',
    icon: (
      <path
        d="M12 17.3l-5.3 3 1.4-6L3 9.9l6.1-.5L12 4l2.9 5.4 6.1.5-5.1 4.4 1.4 6z"
        stroke="currentColor"
        strokeWidth="1.5"
        strokeLinejoin="round"
      />
    ),
  },
  {
    title: 'Bring your own inference provider',
    description: 'Connect Azure OpenAI or any OpenAI-compatible endpoint — you control which model powers your agents.',
    icon: (
      <path
        d="M12 3v4M12 17v4M3 12h4M17 12h4M6.3 6.3l2.8 2.8M14.9 14.9l2.8 2.8M17.7 6.3l-2.8 2.8M9.1 14.9l-2.8 2.8"
        stroke="currentColor"
        strokeWidth="1.75"
        strokeLinecap="round"
      />
    ),
  },
];

export default function HomePage() {
  return (
    <>
      <Hero />

      <div className="border-b border-line bg-paper">
        <div className="mx-auto w-full max-w-6xl px-4 sm:px-6 lg:px-8">
          <TechStrip />
        </div>
      </div>

      <Reveal as="div">
        <Section id="how-it-works" className="relative overflow-hidden bg-paper-2">
          <GridOverlay columns={4} />
          <div className="relative text-center">
            <Eyebrow as="div" className="justify-center">
              Pipeline
            </Eyebrow>
            <h2 className="mt-3 font-display text-3xl font-bold uppercase tracking-tight text-ink sm:text-4xl">
              How it works
            </h2>
          </div>
          <div className="relative mt-10 grid grid-cols-1 gap-6 md:grid-cols-2 lg:grid-cols-4">
            {steps.map((step, index) => (
              <Panel key={step.title} tone="paper" className="p-6">
                <div className="flex h-9 w-9 items-center justify-center border border-line bg-accent-tint font-mono text-sm font-semibold text-accent-strong">
                  {String(index + 1).padStart(2, '0')}
                </div>
                <h3 className="mt-4 font-display text-base font-bold text-ink">{step.title}</h3>
                <p className="mt-2 text-sm text-ink-muted">{step.description}</p>
              </Panel>
            ))}
          </div>
        </Section>
      </Reveal>

      <Reveal as="div">
        <Section id="whats-inside">
          <div className="text-center">
            <Eyebrow as="div" className="justify-center">
              Capabilities
            </Eyebrow>
            <h2 className="mt-3 font-display text-3xl font-bold uppercase tracking-tight text-ink sm:text-4xl">
              What&apos;s inside
            </h2>
          </div>
          <div className="mt-10 grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-3">
            {features.map((feature) => (
              <Panel key={feature.title} tone="paper" className="p-6">
                <svg
                  width="28"
                  height="28"
                  viewBox="0 0 24 24"
                  fill="none"
                  aria-hidden="true"
                  focusable="false"
                  className="text-accent-strong"
                >
                  {feature.icon}
                </svg>
                <h3 className="mt-4 font-display text-base font-bold text-ink">{feature.title}</h3>
                <p className="mt-2 text-sm text-ink-muted">{feature.description}</p>
              </Panel>
            ))}
          </div>
        </Section>
      </Reveal>

      <Reveal as="div">
        <Section id="get-started" className="bg-ink text-white">
          <div className="text-center">
            <Eyebrow as="div" className="justify-center text-white/60">
              Get started
            </Eyebrow>
            <h2 className="mt-3 font-display text-3xl font-bold uppercase tracking-tight text-white sm:text-4xl">
              Ready to see it on your project?
            </h2>
            <p className="mx-auto mt-3 max-w-xl font-mono text-sm text-white/70">
              Tell us about what you&apos;re building and we&apos;ll get in touch.
            </p>
            <div className="mt-8 flex justify-center">
              <Button href="/contact" variant="accent" size="lg" skew>
                Get in touch
              </Button>
            </div>
          </div>
        </Section>
      </Reveal>
    </>
  );
}
