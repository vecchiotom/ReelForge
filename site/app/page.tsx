import Link from 'next/link';
import { Section } from '@/components/layout/Section';
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
      <section className="py-10 sm:py-16 lg:py-20">
        <div className="mx-auto w-full max-w-4xl px-4 text-center sm:px-6 lg:px-8">
          <h1 className="text-3xl font-bold tracking-tight text-neutral-900 sm:text-4xl lg:text-5xl">
            AI agents that turn your codebase into promotional videos
          </h1>
          <p className="mx-auto mt-4 max-w-2xl text-base text-neutral-600 sm:text-lg">
            Agents analyze your project, script and direct a promo video, and Remotion renders it
            — with a review loop that scores and refines every draft.
          </p>
          <div className="mt-8 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-center">
            <Link
              href="/contact"
              className="inline-flex min-h-11 w-full items-center justify-center rounded-md bg-brand-600 px-6 py-3 text-base font-semibold text-white hover:bg-brand-700 sm:w-auto"
            >
              Get in touch
            </Link>
            <a
              href="/app/login"
              className="inline-flex min-h-11 w-full items-center justify-center rounded-md border border-neutral-300 px-6 py-3 text-base font-semibold text-neutral-700 hover:bg-neutral-50 sm:w-auto"
            >
              Sign in
            </a>
          </div>
        </div>
      </section>

      <Section className="bg-neutral-50">
        <h2 className="text-center text-2xl font-bold tracking-tight text-neutral-900 sm:text-3xl">
          How it works
        </h2>
        <div className="mt-10 grid grid-cols-1 gap-6 md:grid-cols-2 lg:grid-cols-4">
          {steps.map((step, index) => (
            <div key={step.title} className="rounded-lg border border-neutral-200 bg-white p-6">
              <div className="flex h-9 w-9 items-center justify-center rounded-full bg-brand-100 text-sm font-semibold text-brand-700">
                {index + 1}
              </div>
              <h3 className="mt-4 text-base font-semibold text-neutral-900">{step.title}</h3>
              <p className="mt-2 text-sm text-neutral-600">{step.description}</p>
            </div>
          ))}
        </div>
      </Section>

      <Section>
        <h2 className="text-center text-2xl font-bold tracking-tight text-neutral-900 sm:text-3xl">
          What&apos;s inside
        </h2>
        <div className="mt-10 grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-3">
          {features.map((feature) => (
            <div key={feature.title} className="rounded-lg border border-neutral-200 p-6">
              <svg
                width="28"
                height="28"
                viewBox="0 0 24 24"
                fill="none"
                aria-hidden="true"
                focusable="false"
                className="text-brand-600"
              >
                {feature.icon}
              </svg>
              <h3 className="mt-4 text-base font-semibold text-neutral-900">{feature.title}</h3>
              <p className="mt-2 text-sm text-neutral-600">{feature.description}</p>
            </div>
          ))}
        </div>
      </Section>

      <Section className="bg-brand-600">
        <div className="text-center">
          <h2 className="text-2xl font-bold tracking-tight text-white sm:text-3xl">
            Ready to see it on your project?
          </h2>
          <p className="mx-auto mt-3 max-w-xl text-brand-50">
            Tell us about what you&apos;re building and we&apos;ll get in touch.
          </p>
          <Link
            href="/contact"
            className="mt-8 inline-flex min-h-11 items-center justify-center rounded-md bg-white px-6 py-3 text-base font-semibold text-brand-700 hover:bg-brand-50"
          >
            Get in touch
          </Link>
        </div>
      </Section>
    </>
  );
}
