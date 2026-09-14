import { Container } from '@/components/layout/Container';
import { Section } from '@/components/layout/Section';
import { Eyebrow } from '@/components/ui/Eyebrow';
import { ContactAddress } from '@/components/contact/ContactAddress';
import { Reveal } from '@/components/motion/Reveal';
import { buildMetadata } from '@/lib/seo';

export const metadata = buildMetadata({
  title: 'About',
  description:
    "How ReelForge is built: a self-hostable microservices platform where specialized AI agents produce and edit promotional video from your own codebase.",
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
          <Eyebrow>The platform</Eyebrow>
          <h1 className="mt-4 text-balance font-display text-[clamp(2rem,5vw,3.5rem)] font-bold uppercase leading-[0.95] tracking-[-0.02em] text-ink">
            About
          </h1>
        </Container>
      </div>

      <Reveal as="div">
        <Section className="border-b border-line">
          <Eyebrow>Mission</Eyebrow>
          <h2 className="mt-3 font-display text-2xl font-bold uppercase tracking-[-0.01em] text-ink">
            What we&apos;re building
          </h2>
          <div className="mt-4 max-w-3xl space-y-4 text-ink-muted">
            <p>
              ReelForge is an agentic platform for generating promotional videos. Instead of a
              template picker, a pipeline of specialized AI agents reads your codebase and assets,
              works out what your product actually does and looks like, and produces a video plan
              that Remotion renders programmatically.
            </p>
            <p>
              The same platform also handles real source footage: ffmpeg-based analysis finds
              silence and shot boundaries in raw video, an editorial agent decides what to keep, and
              a compile step cuts and layers motion graphics into the final render. A review agent
              scores every draft and can send it back through production before it ships.
            </p>
          </div>
        </Section>
      </Reveal>

      <Reveal as="div">
        <Section className="border-b border-line bg-paper-2">
          <Eyebrow>Architecture</Eyebrow>
          <h2 className="mt-3 font-display text-2xl font-bold uppercase tracking-[-0.01em] text-ink">
            How we work
          </h2>
          <div className="mt-4 max-w-3xl space-y-4 text-ink-muted">
            <p>
              ReelForge is built as a set of microservices — a Go API for authentication, a REST API
              for projects and workflows, and a workflow engine that executes agent runs consumed
              from a message queue. All of it is self-hostable via Docker Compose.
            </p>
            <p>
              You bring your own inference provider — Azure OpenAI or any OpenAI-compatible
              endpoint — so you control which model powers your agents. Your project data, files,
              and workflow history stay in your own PostgreSQL database and object storage, not a
              third-party data lake.
            </p>
            <AccentList
              items={[
                'Self-hostable end to end via Docker Compose',
                'Bring your own model endpoints (Azure OpenAI or OpenAI-compatible)',
                'Your data stays in your own Postgres and object storage',
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
              Want to talk through how ReelForge fits your project? Reach out and we&apos;ll get
              back to you.
            </p>
            <ContactAddress className="mt-4" />
          </div>
        </Section>
      </Reveal>
    </>
  );
}
