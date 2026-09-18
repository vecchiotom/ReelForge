import Link from 'next/link';
import { Container } from '@/components/layout/Container';
import { Section } from '@/components/layout/Section';
import { Eyebrow } from '@/components/ui/Eyebrow';
import { ContactAddress } from '@/components/contact/ContactAddress';
import { Reveal } from '@/components/motion/Reveal';
import { JsonLd } from '@/components/seo/JsonLd';
import { buildMetadata } from '@/lib/seo';
import { breadcrumbSchema } from '@/lib/structured-data';

export const metadata = buildMetadata({
  title: 'About',
  description:
    'Why ReelForge exists: giving product teams an AI video team of their own, so launch videos stop being the thing that never gets made.',
  path: '/about',
});

function AccentList({ items }: { items: string[] }) {
  return (
    <ul className="space-y-2">
      {items.map((item) => (
        <li key={item} className="flex items-start gap-3">
          <span aria-hidden="true" className="mt-1.5 h-2 w-2 shrink-0 bg-accent" />
          <span>{item}</span>
        </li>
      ))}
    </ul>
  );
}

export default function AboutPage() {
  return (
    <>
      <div className="border-b border-line pt-16 pb-10 sm:pt-20 lg:pt-24">
        <Container>
          <Eyebrow>Why we built it</Eyebrow>
          <h1 className="mt-4 text-balance font-display text-[clamp(2rem,5vw,3.5rem)] font-bold uppercase leading-[0.95] tracking-[-0.02em] text-ink">
            About
          </h1>
        </Container>
      </div>

      <Reveal as="div">
        <Section className="border-b border-line">
          <Eyebrow>Mission</Eyebrow>
          <h2 className="mt-3 font-display text-2xl font-bold uppercase tracking-[-0.01em] text-ink">
            Good products deserve to be seen
          </h2>
          <div className="mt-4 max-w-3xl space-y-4 text-ink-muted">
            <p>
              Teams spend months building something genuinely good, then announce it with a
              screenshot. Not because they do not know video works, but because making one means
              money they would rather spend elsewhere, or a week of somebody&apos;s time they do
              not have.
            </p>
            <p>
              We think that trade-off should not exist. ReelForge is an AI video team you can point
              at your own product: it works out what the product does, decides how to show it,
              edits the footage, designs the graphics, and hands you a finished video. Not a
              template with your logo dropped in — a video built around the thing you actually
              made.
            </p>
            <p>
              Curious what that covers in practice?{' '}
              <Link href="/features" className="font-medium text-accent-strong underline underline-offset-4">
                See the features
              </Link>
              .
            </p>
          </div>
        </Section>
      </Reveal>

      <Reveal as="div">
        <Section className="border-b border-line bg-paper-2">
          <Eyebrow>Approach</Eyebrow>
          <h2 className="mt-3 font-display text-2xl font-bold uppercase tracking-[-0.01em] text-ink">
            How we think about it
          </h2>
          <div className="mt-4 max-w-3xl space-y-4 text-ink-muted">
            <p>
              Most AI video tools are impressive for thirty seconds and useless for marketing,
              because they invent things. That is fine for a mood piece and fatal for a product
              demo: a video that shows a feature you do not have costs you more than no video at
              all.
            </p>
            <p>
              So ReelForge is built the opposite way round. The creative decisions are made by AI,
              but every frame is anchored to something real — your product, your footage, your
              words. The agents choose what to show; they do not get to make it up. And the last
              agent in the chain is a reviewer whose only job is to reject work that is not good
              enough yet.
            </p>
            <AccentList
              items={[
                'Every video is built from your real product and your real footage',
                'Creative judgement from AI, factual grounding from your material',
                'Every draft scored and reworked before a human is asked to look at it',
              ]}
            />
          </div>
        </Section>
      </Reveal>

      <Reveal as="div">
        <Section className="border-b border-line">
          <Eyebrow>Ownership</Eyebrow>
          <h2 className="mt-3 font-display text-2xl font-bold uppercase tracking-[-0.01em] text-ink">
            You keep the keys
          </h2>
          <div className="mt-4 max-w-3xl space-y-4 text-ink-muted">
            <p>
              Marketing videos get made from unreleased features, internal recordings and material
              nobody wants sitting on a vendor&apos;s servers. That is why ReelForge is built to run
              inside your own environment, connected to your own AI provider account.
            </p>
            <p>
              You decide which models see your material, and where your files and history live. If
              your security team needs everything to stay in-house, it can.
            </p>
            <AccentList
              items={[
                'Runs on your own infrastructure, end to end',
                'Connects to the AI provider account you already have',
                'Your projects, footage and history stay in storage you control',
              ]}
            />
          </div>
        </Section>
      </Reveal>

      <Reveal as="div">
        <Section>
          <Eyebrow>Get in touch</Eyebrow>
          <h2 className="mt-3 font-display text-2xl font-bold uppercase tracking-[-0.01em] text-ink">
            Contact
          </h2>
          <div className="mt-4 max-w-3xl text-ink-muted">
            <p>
              Want to talk through whether ReelForge fits how your team works?{' '}
              <Link href="/contact" className="font-medium text-accent-strong underline underline-offset-4">
                Send us a note
              </Link>{' '}
              and we will get back to you.
            </p>
            <ContactAddress className="mt-4" />
          </div>
        </Section>
      </Reveal>

      <JsonLd
        data={breadcrumbSchema([
          { name: 'Home', path: '/' },
          { name: 'About', path: '/about' },
        ])}
      />
    </>
  );
}
