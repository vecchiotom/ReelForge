import { Container } from '@/components/layout/Container';
import { Section } from '@/components/layout/Section';
import { buildMetadata } from '@/lib/seo';

export const metadata = buildMetadata({
  title: 'About',
  description:
    "How ReelForge is built: a self-hostable microservices platform where specialized AI agents produce and edit promotional video from your own codebase.",
  path: '/about',
});

export default function AboutPage() {
  return (
    <>
      <div className="pt-12 sm:pt-16 lg:pt-20">
        <Container>
          <h1 className="text-3xl font-bold tracking-tight text-neutral-900 sm:text-4xl lg:text-5xl">
            About
          </h1>
        </Container>
      </div>

      <Section>
        <h2 className="text-2xl font-bold tracking-tight text-neutral-900">
          What we&apos;re building
        </h2>
        <div className="mt-4 max-w-3xl space-y-4 text-neutral-600">
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

      <Section className="bg-neutral-50">
        <h2 className="text-2xl font-bold tracking-tight text-neutral-900">How we work</h2>
        <div className="mt-4 max-w-3xl space-y-4 text-neutral-600">
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
          <ul className="list-disc space-y-2 pl-5">
            <li>Self-hostable end to end via Docker Compose</li>
            <li>Bring your own model endpoints (Azure OpenAI or OpenAI-compatible)</li>
            <li>Your data stays in your own Postgres and object storage</li>
          </ul>
        </div>
      </Section>

      <Section>
        <h2 className="text-2xl font-bold tracking-tight text-neutral-900">Contact</h2>
        <div className="mt-4 max-w-3xl text-neutral-600">
          <p>
            Want to talk through how ReelForge fits your project? Reach out and we&apos;ll get
            back to you.
          </p>
          {/*
            Minimal/structural for now. Phase 4 replaces this mailto placeholder and adds
            the real postal address using the bracketed tokens from
            site/lib/legal-placeholders.ts.
          */}
          <address className="not-italic mt-4">
            <a href="mailto:hello@example.com" className="text-brand-600 hover:text-brand-700">
              hello@example.com
            </a>
          </address>
        </div>
      </Section>
    </>
  );
}
