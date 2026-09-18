import { siteConfig } from '@/lib/site-config';
import { HOME_FAQS } from '@/lib/faqs';

// `llms.txt` is the emerging convention for handing AI assistants a clean, plain-text summary of
// a site instead of making them infer it from rendered HTML. Served as a route rather than a
// static file in `public/` so the absolute URLs stay correct for whatever NEXT_PUBLIC_SITE_URL
// this deployment was built with, and so the FAQ answers can never drift from the ones on the
// homepage — both read the same array.
export const dynamic = 'force-static';

function body(): string {
  const { url, name } = siteConfig;

  return `# ${name}

> ${siteConfig.shortDescription}

${name} is an AI video team for software companies. You point it at your product and any raw
footage you already have, and a group of specialised AI agents research the product, write the
script, direct the story, cut the footage, design the on-screen graphics, grade the picture and
choose the sound. A separate reviewer agent scores every draft and sends weak ones back through
production, so what reaches you is a finished, publishable video rather than a first attempt.

## What it is used for

- Product launch videos
- Feature demos and walkthroughs
- Sales outreach and outbound video
- Paid social advertising creative
- Customer onboarding and how-to clips
- Release highlights and investor updates

## What makes it different

- Videos are built from your real product and real footage, not a stock template with a logo
  dropped into it, so what viewers see matches what customers get.
- Raw, unedited recordings can be uploaded directly: pauses, stumbles and weaker takes are
  removed automatically and the cut follows complete sentences.
- Motion graphics, captions, colour grading, background music and sound effects are all part of
  the finished cut, including interfaces tracked onto screens filmed in shot.
- Every draft is scored against the brief by a reviewer agent before a human sees it.
- Customers bring their own AI provider account and can run the whole platform on their own
  infrastructure, so product material and unreleased features stay under their control.

## Pages

- [Home](${url}/): what ${name} does, how it works, and answers to common questions.
- [Features](${url}/features): the production crew, editing, design, brand fidelity, quality
  review, repeatability and data control, in detail.
- [About](${url}/about): why ${name} exists and the principles it is built on.
- [Contact](${url}/contact): book a demo or reach the team.
- [Privacy policy](${url}/legal/privacy)
- [Terms of service](${url}/legal/terms)

## Frequently asked questions

${HOME_FAQS.map((faq) => `### ${faq.question}\n\n${faq.answer}`).join('\n\n')}
`;
}

export function GET() {
  return new Response(body(), {
    headers: {
      'Content-Type': 'text/plain; charset=utf-8',
      'Cache-Control': 'public, max-age=3600, s-maxage=3600',
    },
  });
}
