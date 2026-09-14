import Link from 'next/link';
import { Container } from '@/components/layout/Container';
import { Section } from '@/components/layout/Section';
import { buildMetadata } from '@/lib/seo';

export const metadata = buildMetadata({
  title: 'Features',
  description:
    'Explore the agentic workflow engine, full agent library, Remotion rendering, ffmpeg-based video derushing, and review-loop scoring that power ReelForge.',
  path: '/features',
});

export default function FeaturesPage() {
  return (
    <>
      <div className="pt-12 sm:pt-16 lg:pt-20">
        <Container>
          <h1 className="text-3xl font-bold tracking-tight text-neutral-900 sm:text-4xl lg:text-5xl">
            Features
          </h1>
          <p className="mt-4 max-w-2xl text-lg text-neutral-600">
            A tour of what actually runs under the hood — the agentic workflow engine, the agent
            library, and the video pipeline it drives.
          </p>
        </Container>
      </div>

      <Section>
        <h2 className="text-2xl font-bold tracking-tight text-neutral-900">
          Agentic workflow engine
        </h2>
        <div className="mt-4 max-w-3xl space-y-4 text-neutral-600">
          <p>
            Every video is produced by a workflow: a sequence of steps that runs agents, evaluates
            conditions, loops, and can fan work out in parallel. Workflows are built visually,
            step by step, and executed by a dedicated engine that consumes execution requests off
            a message queue.
          </p>
          <p>
            Step types cover more than plain agent calls — conditionals branch on prior output,
            for-each steps repeat over a list, and review loops send low-scoring output back
            through production automatically.
          </p>
          <ul className="list-disc space-y-2 pl-5">
            <li>Visual, drag-and-drop workflow builder</li>
            <li>Conditional branches, loops, and parallel steps</li>
            <li>Deterministic extract/transform steps alongside LLM agent steps</li>
          </ul>
        </div>
      </Section>

      <Section className="bg-neutral-50">
        <h2 className="text-2xl font-bold tracking-tight text-neutral-900">Agent library</h2>
        <div className="mt-4 max-w-3xl space-y-4 text-neutral-600">
          <p>
            Agents are grouped by what they do in the pipeline, from understanding your codebase
            to producing the final script and video plan.
          </p>
          <ul className="list-disc space-y-2 pl-5">
            <li>
              <strong>Analysis</strong> — inventories code structure, dependencies, components,
              routes, and styling/theming from your project
            </li>
            <li>
              <strong>Translation</strong> — maps analyzed structure and an animation strategy onto
              Remotion components
            </li>
            <li>
              <strong>Production</strong> — directs, scripts, and authors the actual video, plus
              editors that plan story cuts and motion graphics
            </li>
            <li>
              <strong>Quality</strong> — reviews and scores each draft against the brief
            </li>
          </ul>
        </div>
      </Section>

      <Section>
        <h2 className="text-2xl font-bold tracking-tight text-neutral-900">
          Remotion-based rendering
        </h2>
        <div className="mt-4 max-w-3xl space-y-4 text-neutral-600">
          <p>
            Videos aren&apos;t templated slideshows — they&apos;re React components rendered
            frame-by-frame with Remotion, driven by the plan your production agents assemble.
          </p>
          <ul className="list-disc space-y-2 pl-5">
            <li>Programmatic composition, not fixed templates</li>
            <li>Animation strategy derived from your project&apos;s own style and theming</li>
          </ul>
        </div>
      </Section>

      <Section className="bg-neutral-50">
        <h2 className="text-2xl font-bold tracking-tight text-neutral-900">
          Source-video derushing and motion graphics
        </h2>
        <div className="mt-4 max-w-3xl space-y-4 text-neutral-600">
          <p>
            When you bring your own raw footage, ffmpeg-based analysis detects silence and shot
            boundaries, optionally transcribes the audio, and produces a bounded, id-anchored view
            of the footage for an editorial agent to work from — it decides what to keep, never a
            raw timestamp.
          </p>
          <p>
            A separate compile step resolves those decisions back to frame-accurate cuts and can
            layer in motion-graphics overlays — lower-thirds, titles, and callouts — planned by a
            dedicated agent and placed only at deterministic, pre-offered positions.
          </p>
          <ul className="list-disc space-y-2 pl-5">
            <li>Silence and shot detection via ffmpeg</li>
            <li>Optional transcription for spoken content</li>
            <li>Motion-graphics overlays composited during the final encode</li>
          </ul>
        </div>
      </Section>

      <Section>
        <h2 className="text-2xl font-bold tracking-tight text-neutral-900">
          Review-loop quality scoring
        </h2>
        <div className="mt-4 max-w-3xl space-y-4 text-neutral-600">
          <p>
            A review agent scores each production draft. If the score falls short of the
            workflow&apos;s threshold, the pipeline loops back through production automatically
            instead of shipping a weak result.
          </p>
        </div>
      </Section>

      <Section className="bg-neutral-50">
        <h2 className="text-2xl font-bold tracking-tight text-neutral-900">
          Bring your own inference provider
        </h2>
        <div className="mt-4 max-w-3xl space-y-4 text-neutral-600">
          <p>
            ReelForge doesn&apos;t lock you into one model vendor. Configure Azure OpenAI or any
            OpenAI-compatible endpoint per agent, or set a default provider for chat, transcription,
            and vision workloads independently.
          </p>
          <ul className="list-disc space-y-2 pl-5">
            <li>Azure OpenAI or OpenAI-compatible endpoints</li>
            <li>Per-agent provider overrides</li>
            <li>Self-hostable end to end via Docker Compose</li>
          </ul>
        </div>
      </Section>

      <Section>
        <div className="rounded-lg bg-brand-600 px-6 py-10 text-center sm:px-12">
          <h2 className="text-2xl font-bold tracking-tight text-white sm:text-3xl">
            Want to see this on your own project?
          </h2>
          <Link
            href="/contact"
            className="mt-6 inline-flex min-h-11 items-center justify-center rounded-md bg-white px-6 py-3 text-base font-semibold text-brand-700 hover:bg-brand-50"
          >
            Get in touch
          </Link>
        </div>
      </Section>
    </>
  );
}
