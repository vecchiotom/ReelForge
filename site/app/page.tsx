import Link from 'next/link';
import { Section } from '@/components/layout/Section';
import { Panel } from '@/components/ui/Panel';
import { Eyebrow } from '@/components/ui/Eyebrow';
import { Button } from '@/components/ui/Button';
import { GridOverlay } from '@/components/ui/GridOverlay';
import { Hero } from '@/components/home/Hero';
import { UseCaseStrip } from '@/components/home/UseCaseStrip';
import { Reveal } from '@/components/motion/Reveal';
import { JsonLd } from '@/components/seo/JsonLd';
import { buildMetadata } from '@/lib/seo';
import { faqSchema, softwareApplicationSchema } from '@/lib/structured-data';
import { HOME_FAQS } from '@/lib/faqs';

export const metadata = buildMetadata({
  title: 'AI Video Team for Product Marketing',
  description:
    'ReelForge turns your product into finished promo videos. AI agents write the script, cut your footage, design the graphics, and score every draft before you publish.',
  path: '/',
});

const steps = [
  {
    title: 'Show it your product',
    description:
      'Connect your product and drop in whatever you already have — screen recordings, raw footage, brand assets, docs. No brief to write.',
  },
  {
    title: 'Agents plan the video',
    description:
      'A team of AI agents works out what your product does, who it is for, and which moments are worth showing, then writes and storyboards the video.',
  },
  {
    title: 'They edit and design it',
    description:
      'Dead air and bad takes get cut, the best moments get kept, and titles, captions, colour and sound are added — the work an editor would do.',
  },
  {
    title: 'You get a finished cut',
    description:
      'Download a ready-to-publish video. Ask for a different angle, a shorter version, or a new one for every release — it costs you a click.',
  },
];

const features = [
  {
    title: 'A whole production team, on demand',
    description:
      'Scriptwriter, director, editor, motion designer, colourist and sound. Every role an agency would bill you for, working in parallel on your video.',
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
    title: 'Your raw footage, already edited',
    description:
      'Upload an unedited recording and get a tight cut back. Silence, stumbles and weak takes are removed automatically, and the story is kept intact.',
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
    title: 'On brand, every single time',
    description:
      'Videos are built from your own product and its real look and feel, so the result matches what customers actually see instead of a stock template.',
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
    title: 'Nothing weak gets published',
    description:
      'A reviewer agent scores every draft against the brief. Anything that falls short goes straight back into production — before it ever reaches you.',
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
    title: 'Repeat it for every release',
    description:
      'Save the way you make videos as a reusable recipe. Ship a feature, run it again, and have the launch video ready the same day.',
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
    title: 'Your AI, your data, your servers',
    description:
      'Bring your own AI provider and run the whole platform inside your own infrastructure. Your product and footage never become somebody else’s training data.',
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

const outcomes = [
  {
    stat: 'Days, not weeks',
    label: 'From “we should make a video” to a finished cut, without booking an agency.',
  },
  {
    stat: 'One video per release',
    label: 'Launch content stops being the thing that slips when the sprint runs long.',
  },
  {
    stat: 'No editing seat required',
    label: 'Marketing gets to ship video without waiting on a designer or an editor.',
  },
];

export default function HomePage() {
  return (
    <>
      <Hero />

      <div className="border-b border-line bg-paper">
        <div className="mx-auto w-full max-w-6xl px-4 sm:px-6 lg:px-8">
          <UseCaseStrip />
        </div>
      </div>

      <Reveal as="div">
        <Section id="why" className="border-b border-line">
          <div className="grid gap-10 lg:grid-cols-12">
            <div className="lg:col-span-5">
              <Eyebrow>The problem</Eyebrow>
              <h2 className="mt-3 font-display text-3xl font-bold uppercase tracking-tight text-ink sm:text-4xl">
                Great products, invisible launches
              </h2>
            </div>
            <div className="space-y-4 text-ink-muted lg:col-span-7">
              <p>
                Video is the format buyers actually watch, and it is the one thing most teams never
                get around to making. An agency costs thousands and takes weeks. Doing it in-house
                means someone stops their real job to fight with an editing timeline.
              </p>
              <p>
                So the feature ships, the changelog goes out, and nobody outside the team ever sees
                the product move. ReelForge closes that gap: it gives you the production team
                without the headcount, the retainer, or the two-week turnaround.
              </p>
            </div>
          </div>

          <div className="mt-12 grid grid-cols-1 gap-6 md:grid-cols-3">
            {outcomes.map((outcome) => (
              <Panel key={outcome.stat} tone="paper" className="p-6">
                <p className="font-display text-xl font-bold uppercase tracking-tight text-accent-strong">
                  {outcome.stat}
                </p>
                <p className="mt-2 text-sm text-ink-muted">{outcome.label}</p>
              </Panel>
            ))}
          </div>
        </Section>
      </Reveal>

      <Reveal as="div">
        <Section id="how-it-works" className="relative overflow-hidden bg-paper-2">
          <GridOverlay columns={4} />
          <div className="relative text-center">
            <Eyebrow as="div" className="justify-center">
              How it works
            </Eyebrow>
            <h2 className="mt-3 font-display text-3xl font-bold uppercase tracking-tight text-ink sm:text-4xl">
              Four steps to a finished video
            </h2>
            <p className="mx-auto mt-4 max-w-2xl text-ink-muted">
              You bring the product. ReelForge handles everything between the idea and the export.
            </p>
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
          <p className="relative mt-8 text-center text-sm text-ink-muted">
            Want the detail behind each step?{' '}
            <Link href="/features" className="font-medium text-accent-strong underline underline-offset-4">
              See the full feature breakdown
            </Link>
            .
          </p>
        </Section>
      </Reveal>

      <Reveal as="div">
        <Section id="whats-inside">
          <div className="text-center">
            <Eyebrow as="div" className="justify-center">
              What you get
            </Eyebrow>
            <h2 className="mt-3 font-display text-3xl font-bold uppercase tracking-tight text-ink sm:text-4xl">
              Everything an in-house video team would do
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
        <Section id="faq" className="border-t border-line bg-paper-2">
          <div className="text-center">
            <Eyebrow as="div" className="justify-center">
              Questions
            </Eyebrow>
            <h2 className="mt-3 font-display text-3xl font-bold uppercase tracking-tight text-ink sm:text-4xl">
              Frequently asked
            </h2>
          </div>
          <dl className="mx-auto mt-10 max-w-3xl divide-y divide-line border-y border-line">
            {HOME_FAQS.map((faq) => (
              <div key={faq.question} className="py-6">
                <dt className="font-display text-base font-bold text-ink">{faq.question}</dt>
                <dd className="mt-2 text-sm leading-relaxed text-ink-muted">{faq.answer}</dd>
              </div>
            ))}
          </dl>
          <p className="mt-8 text-center text-sm text-ink-muted">
            Still deciding?{' '}
            <Link href="/about" className="font-medium text-accent-strong underline underline-offset-4">
              Read what we are building and why
            </Link>
            .
          </p>
        </Section>
      </Reveal>

      <Reveal as="div">
        <Section id="get-started" className="bg-ink text-white">
          <div className="text-center">
            <Eyebrow as="div" className="justify-center text-white/60">
              Get started
            </Eyebrow>
            <h2 className="mt-3 font-display text-3xl font-bold uppercase tracking-tight text-white sm:text-4xl">
              See it run on your own product
            </h2>
            <p className="mx-auto mt-3 max-w-xl font-mono text-sm text-white/70">
              Tell us what you are building and what you want to show off. We will walk you through
              a video made from your product.
            </p>
            <div className="mt-8 flex justify-center">
              <Button href="/contact" variant="accent" size="lg" skew>
                Book a demo
              </Button>
            </div>
          </div>
        </Section>
      </Reveal>

      <JsonLd data={softwareApplicationSchema()} />
      <JsonLd data={faqSchema(HOME_FAQS)} />
    </>
  );
}
