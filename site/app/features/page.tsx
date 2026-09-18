import type { ReactNode } from 'react';
import Link from 'next/link';
import { Container } from '@/components/layout/Container';
import { Section } from '@/components/layout/Section';
import { Eyebrow } from '@/components/ui/Eyebrow';
import { Button } from '@/components/ui/Button';
import { Reveal } from '@/components/motion/Reveal';
import { JsonLd } from '@/components/seo/JsonLd';
import { buildMetadata } from '@/lib/seo';
import { breadcrumbSchema } from '@/lib/structured-data';

export const metadata = buildMetadata({
  title: 'Features',
  description:
    'See how ReelForge scripts, edits, designs and reviews your promotional videos — from raw footage to a finished cut, with your own AI provider and your data under your control.',
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
          <Eyebrow>What ReelForge does</Eyebrow>
          <h1 className="mt-4 text-balance font-display text-[clamp(2rem,5vw,3.5rem)] font-bold uppercase leading-[0.95] tracking-[-0.02em] text-ink">
            Features
          </h1>
          <p className="mt-4 max-w-2xl font-mono text-sm leading-relaxed text-ink-muted">
            Every job a video team does, handled by AI agents — and reviewed before it reaches you.
          </p>
        </Container>
      </div>

      <Reveal as="div">
        <Section className="border-b border-line">
          <Eyebrow>The team</Eyebrow>
          <h2 className="mt-3 font-display text-2xl font-bold uppercase tracking-[-0.01em] text-ink">
            A production crew that never books out
          </h2>
          <div className="mt-4 max-w-3xl space-y-4 text-ink-muted">
            <p>
              A good promo video is not one job, it is a dozen. Someone has to understand the
              product, decide the story, write the words, pick the shots, design the titles, grade
              the picture, and choose the music. ReelForge gives you a specialist agent for each of
              those roles, and they work on your video at the same time.
            </p>
            <AccentList
              items={[
                'A researcher that learns what your product does and who it is for',
                'A scriptwriter and a director that turn that into a story worth watching',
                'An editor, a motion designer, a colourist and a sound designer that finish it',
                'A reviewer that scores the result and sends weak drafts back',
              ]}
            />
            <p>
              For bigger decisions, several agents argue it out before committing — a cut, a set of
              on-screen graphics or a colour treatment is debated from different angles instead of
              being the first idea that came up.
            </p>
          </div>
        </Section>
      </Reveal>

      <Reveal as="div">
        <Section className="border-b border-line bg-paper-2">
          <Eyebrow>Editing</Eyebrow>
          <h2 className="mt-3 font-display text-2xl font-bold uppercase tracking-[-0.01em] text-ink">
            Upload the raw take, get back the finished cut
          </h2>
          <div className="mt-4 max-w-3xl space-y-4 text-ink-muted">
            <p>
              Record your demo in one go, mistakes and all. ReelForge watches and listens to the
              whole thing, finds where you paused, restarted or lost the thread, and keeps only the
              moments that carry the story. What comes back is tight, properly paced, and cut on
              full sentences rather than mid-word.
            </p>
            <p>
              If you shot several takes, it recognises the repeats and keeps the best one. You never
              scrub a timeline looking for the good bit.
            </p>
            <AccentList
              items={[
                'Dead air, stumbles and false starts removed automatically',
                'The strongest take chosen when you recorded the same thing twice',
                'Spoken words transcribed so the edit follows what was actually said',
                'Smooth transitions and a clean fade in and out, handled for you',
              ]}
            />
          </div>
        </Section>
      </Reveal>

      <Reveal as="div">
        <Section className="border-b border-line">
          <Eyebrow>Design</Eyebrow>
          <h2 className="mt-3 font-display text-2xl font-bold uppercase tracking-[-0.01em] text-ink">
            Titles, captions and graphics that look designed
          </h2>
          <div className="mt-4 max-w-3xl space-y-4 text-ink-muted">
            <p>
              Lower thirds, headline cards, callouts pointing at the thing you just mentioned — the
              overlays that separate a polished video from a screen recording. They are placed where
              they will not cover anything important, and timed to what is being said.
            </p>
            <p>
              Filming a phone or a laptop in shot? ReelForge can drop your real interface onto the
              screen and keep it locked there as the device moves, so the product looks live instead
              of taped on.
            </p>
            <AccentList
              items={[
                'Lower thirds, titles and callouts timed to the narration',
                'Your interface tracked onto screens filmed in the shot',
                'Colour grading applied across the whole video for a consistent look',
                'Background music and sound effects that sit under the voice, not over it',
              ]}
            />
          </div>
        </Section>
      </Reveal>

      <Reveal as="div">
        <Section className="border-b border-line bg-paper-2">
          <Eyebrow>On brand</Eyebrow>
          <h2 className="mt-3 font-display text-2xl font-bold uppercase tracking-[-0.01em] text-ink">
            Built from your product, not a template
          </h2>
          <div className="mt-4 max-w-3xl space-y-4 text-ink-muted">
            <p>
              Most video tools hand you an animation somebody else designed and let you swap the
              logo. ReelForge works the other way round: it studies your actual product — the
              screens, the colours, the type, the way it moves — and builds the video out of that.
            </p>
            <p>
              The result is a video that matches what a customer sees after they sign up. No stock
              footage of strangers pointing at laptops, no generic gradient that could belong to
              anyone.
            </p>
          </div>
        </Section>
      </Reveal>

      <Reveal as="div">
        <Section className="border-b border-line">
          <Eyebrow>Quality</Eyebrow>
          <h2 className="mt-3 font-display text-2xl font-bold uppercase tracking-[-0.01em] text-ink">
            Nothing mediocre reaches your inbox
          </h2>
          <div className="mt-4 max-w-3xl space-y-4 text-ink-muted">
            <p>
              Every draft is scored before you ever see it. A reviewer agent checks the cut against
              the brief — does it hold together, does it say what it set out to say, does the
              pacing work — and anything that falls short goes straight back into production for
              another pass.
            </p>
            <p>
              You review a version the system already believes is good, not a first attempt.
            </p>
          </div>
        </Section>
      </Reveal>

      <Reveal as="div">
        <Section className="border-b border-line bg-paper-2">
          <Eyebrow>Repeatability</Eyebrow>
          <h2 className="mt-3 font-display text-2xl font-bold uppercase tracking-[-0.01em] text-ink">
            One video, then one every release
          </h2>
          <div className="mt-4 max-w-3xl space-y-4 text-ink-muted">
            <p>
              The way your video gets made is saved as a reusable recipe. Ship a feature, run it
              again, and the launch video is ready the same day — same structure, same tone, new
              content.
            </p>
            <AccentList
              items={[
                'Save the steps that made a video you liked and reuse them',
                'Adjust one stage without rebuilding the whole thing',
                'Produce several cuts at once — a long one for the site, a short one for social',
              ]}
            />
          </div>
        </Section>
      </Reveal>

      <Reveal as="div">
        <Section>
          <Eyebrow>Control</Eyebrow>
          <h2 className="mt-3 font-display text-2xl font-bold uppercase tracking-[-0.01em] text-ink">
            Your AI, your data, your servers
          </h2>
          <div className="mt-4 max-w-3xl space-y-4 text-ink-muted">
            <p>
              ReelForge does not lock you into one AI vendor. Connect your own provider account and
              choose which models do the work — including models you host yourself, if your
              security team requires it.
            </p>
            <p>
              The whole platform can run inside your own infrastructure. Your product, your
              unreleased features and your raw footage stay where you put them, and never become
              someone else&apos;s training data.
            </p>
            <AccentList
              items={[
                'Bring your own AI provider account',
                'Pick a different model for different jobs, or keep one for everything',
                'Run it entirely on your own infrastructure',
                'Your files and history stay in storage you control',
              ]}
            />
            <p>
              More on the thinking behind this on the{' '}
              <Link href="/about" className="font-medium text-accent-strong underline underline-offset-4">
                about page
              </Link>
              .
            </p>
          </div>
        </Section>
      </Reveal>

      <div className="bg-ink text-white">
        <Container>
          <div className="flex flex-col items-center gap-6 border-x border-white/10 px-6 py-16 text-center sm:px-12">
            <h2 className="font-display text-2xl font-bold uppercase tracking-[-0.01em] sm:text-3xl">
              Want to see this on your own product?
            </h2>
            <Button href="/contact" variant="accent" size="lg" skew>
              Book a demo
            </Button>
          </div>
        </Container>
      </div>

      <JsonLd
        data={breadcrumbSchema([
          { name: 'Home', path: '/' },
          { name: 'Features', path: '/features' },
        ])}
      />
    </>
  );
}
