# ReelForge Marketing Strategy

Compiled 2026-09-18. Builds on [`market-research.md`](market-research.md). Every recommendation is
constrained by what the codebase supports today (upload-only input, one MP4, no TTS, no billing,
no hosted tier, contact-form-only conversion). Where a recommendation needs a product change, it
says so and treats that change as a prerequisite, not an assumption.

---

## 1. The bet in one paragraph

Win one unoccupied position before anyone else names it: **the video compiles from the code**.
Lead with Pipeline A (source-native promo video) for engineering-led B2B software teams, sell it
sales-led to a small set of design partners in the next 90 days, prove the output in public by
making ReelForge's own videos with ReelForge, and use the Remotion and coding-agent communities as
the first channel. Hold Pipeline B (footage derushing) as the second wedge for studios and agencies
once the first has three public proofs. Do not spend a euro on paid acquisition until the site can
receive a lead and the offer has a price.

Why this order: the research found no competitor that renders a product from its source, several
that already say "agentic", and a buyer population that is burned by AI slop and increasingly
demands self-hosting and bring-your-own-model. Those three facts point at the same story and the
same first customer.

---

## 2. Positioning decision

| Element | Decision |
|---|---|
| Category | Source-native product video. A new sub-category of product marketing video, framed against "recording-based" tools rather than against AI video generators. |
| One-liner | ReelForge compiles a promotional video from your codebase, on your own infrastructure. |
| Mechanism name | Source-Native Rendering (Pipeline A). Never-a-Timestamp Editing and The Edit Room (Pipeline B). |
| Enemy | The screen recording. It captures a moment; the product is code. |
| What we are not | Not an avatar tool, not a prompt-to-video toy, not a template library, not a cloud SaaS. |
| Proof pillars | Real components rendered as React. Review loop with a score threshold. Docker Compose, own Postgres and storage, own model endpoint. Decisions by measured id only, transcripts saved. |
| Tone | Engineer-credible, declarative, mechanism over adjectives. Matches the existing site voice and the "no invented facts" rule. |

Recommended hero replacement for the current "AI agents that turn your codebase into promotional
videos":

- Headline: **Stop recording your product. Compile it.**
- Sub: ReelForge reads your source tree, renders your real screens with Remotion, scripts and
  directs a promo, and scores every draft before you see it. Self-hosted. Your model. Your box.
- Eyebrow: Source-native video · self-hostable

Keep "Get in touch" as the CTA until section 5's prerequisites are done, then add "Run it on your
repo" as a pilot request.

---

## 3. Segment prioritization

Scored 1 to 5 on fit with what ships today, pain intensity, reachability with zero budget, and
willingness to self-host.

| Segment | Fit | Pain | Reach | Self-host tolerance | Total | Role |
|---|---|---|---|---|---|---|
| Engineering-led B2B SaaS and dev-tool teams, 3 to 50 people, shipping every 1 to 4 weeks | 5 | 5 | 5 | 4 | 19 | **Tier 1: first customers and first proofs** |
| Product video studios and small agencies serving software clients | 4 | 4 | 3 | 3 | 14 | **Tier 2: throughput buyer, channel partner, first Pipeline B buyer** |
| Security-sensitive or regulated software vendors (fintech, health, gov suppliers) | 4 | 3 | 2 | 5 | 14 | Tier 2: highest willingness to pay for self-hosting, longest cycle |
| Solo indie hackers launching on Product Hunt | 3 | 5 | 5 | 1 | 14 | Audience, not customer, until a hosted tier exists. Use for reach and proof. |
| Creators and YouTubers with raw footage | 2 | 4 | 4 | 1 | 11 | Later. Needs hosted tier, vertical export, subtitles. |
| Enterprise L&D and comms | 1 | 2 | 1 | 4 | 8 | Ignore. Synthesia and Guidde own it. |

Tier 1 ideal customer profile, for qualification:

- Ships a web product with a React or similar component-based frontend (the analysis and translation agents are built around components, routes and theme tokens).
- Has a release cadence and a public launch channel (Product Hunt, Show HN, changelog, LinkedIn).
- Has at least one engineer comfortable with Docker Compose and an Azure OpenAI or OpenAI-compatible key.
- Has no dedicated video person and has either paid an agency once or tried an AI video tool and rejected the output.
- Disqualifiers today: needs vertical or multi-format output as the primary deliverable; needs AI narration audio; cannot run a 16-service compose stack; wants a browser signup.

---

## 4. Offer and pricing hypothesis

There is no pricing today, so this is a hypothesis to test with design partners, not a price list.

**Phase 1 offer: Design Partner Pilot (now to day 90).** Five to eight teams. ReelForge runs on
their infrastructure with hands-on setup help. In exchange: a public case video, a quote, and weekly
feedback. Charge a nominal setup fee so the commitment is real, in the range a single Fiverr gig
costs, and defer licence pricing to the end of the pilot.

**Phase 2 offer: Self-hosted licence (from day 90).** Anchor against what the buyer pays today, not
against SaaS seats.

| Anchor | What the buyer pays today | Implication |
|---|---|---|
| One agency explainer | $4k to $10k, 4 to 8 weeks | A yearly licence at the price of one agency video is an easy yes for any team that ships more than one video a year |
| Product-demo SaaS | $38 to $200 per month per seat plus credits | Do not price per seat; a 3-person team hates seats. Price per deployment or per project |
| Editor retainer | $2,500 to $10,000 per month | Pipeline B add-on can anchor here for studios |
| Remotion Automators licence | $0.01 per render, $100 per month minimum for companies above 3 employees | Must be disclosed and either passed through or bundled. Never hide it |

Hypothesis to test: a flat annual self-hosted licence per deployment for Tier 1, priced at roughly one
agency video, with Pipeline B as a second line item for studios. Model costs stay with the customer
by design; make that a selling point ("no credit meter") rather than an apology.

**Phase 3 (conditional): hosted "try it on your repo".** The single biggest conversion gap is that
an indie founder cannot try the product without a VM. A narrow hosted tier that runs only the Quick
Win Promo template on an uploaded zip, with a watermark, would turn the Product Hunt and Show HN
audience into a funnel. This requires multi-tenancy, billing and a repo-upload path that do not exist.
Decide after the first ten pilots, not before.

---

## 5. Prerequisites before any outreach

None of these are marketing. All of them block marketing. In order:

1. **Wire lead delivery.** Set `CONTACT_WEBHOOK_URL` so submissions reach Slack, email or a CRM. Today they only reach container logs.
2. **Fill the legal placeholders** in `site/lib/legal-placeholders.ts`. The contact page shows `[RESPONSE_TIME_SLA]` and `[COMPANY_LEGAL_NAME]` to visitors.
3. **Finish the launch checklist** in `docs/marketing-site.md`: real `NEXT_PUBLIC_SITE_URL`, production build target, GA4 id, sitemap submission.
4. **Make ReelForge's own video with ReelForge.** Run the Quick Win Promo template on this repository's `web/` and `site/` trees and put the resulting MP4 on the homepage in place of the abstract "Render preview" panel. This is the single most persuasive asset the company can own and it costs one workflow run. Publish the review-loop scores and the execution transcript next to it.
5. **Produce two more public samples** on open-source apps with permission (a component-heavy OSS dashboard is ideal), so a prospect sees three different products rendered before asking for theirs.
6. **Publish a deployment guide and a system requirements page.** Self-hosting is the product; the setup path is the funnel. Include the Remotion licence note.
7. **Write the "what it does not do yet" page.** Upload not git, one 1080p MP4, no narration audio, no vertical export. Level-4 buyers trust a vendor that lists limits, and it prevents the first ten sales calls from ending on a surprise.
8. **Add a "Request a pilot" path** on the contact form: a checkbox and a field for stack and release cadence, so Tier 1 leads self-identify.

Product changes worth pulling forward because they remove the top objections found in research, in
priority order: repo URL or GitHub import (removes "upload a zip" friction and enables the
"regenerate on every release" story literally), an approval gate between analyze and compile (the
docs already call it the highest-value phase-2 item), vertical and square output variants (every
launch channel wants them), and optional TTS via the same bring-your-own-provider pattern (turns
voiceover text into a finished asset without contradicting the "nothing faked" stance, as long as
it is disclosed and labelled).

---

## 6. Messaging architecture

| Layer | Message |
|---|---|
| Hero | Stop recording your product. Compile it. |
| Mechanism | Source-Native Rendering: agents read your components, routes and theme tokens, render your real screens as Remotion React, script and direct the story, and score every draft. |
| Differentiators | Real UI, not recordings or avatars. Regenerates on every release. Self-hosted with your model. Decisions bounded by measured ids and saved as transcripts. |
| Proof | The ReelForge video made by ReelForge, plus two OSS samples, each with its score history and transcript. |
| Objection handling | See table below. |
| CTA | Run it on your repo (pilot request). |

Objections a Level-4 buyer will raise, with the answer the code supports:

| Objection | Answer |
|---|---|
| "Another AI video tool. It will look generated." | It cannot generate a scene. It renders your components. Nothing in the frame exists outside your repo or your footage. |
| "It took someone 100 prompts with Claude Code and Remotion." | That is the manual version. ReelForge runs the analysis, translation, direction and render as a pipeline with a review loop, so the hundred prompts happen without you. |
| "Where does my source go?" | Into the MinIO bucket on your VM. The stack is Docker Compose; the model endpoint is yours. |
| "How do I know it did not cut a sentence in half?" | The editor never sees a timestamp. It picks ids the silence and shot detectors measured. The schema has no numeric fields, enforced by tests. |
| "Sixteen services is a lot." | It is one compose file and one `.env`. Here is the deployment guide and the requirements page. |
| "No voiceover?" | Correct. You get the script; record it or run it through your own TTS. No synthetic presenter in your video by default. |
| "What about the Remotion licence?" | Free under four employees. Above that, Remotion charges $0.01 per render with a $100 monthly minimum, and it is in our pricing sheet. |

Words to stop using on the site: "agentic" as a headline noun (every competitor uses it), "in
minutes" (saturated and not true once setup is counted), "full agent library" (says nothing to a
buyer). Words to use: compile, render, source, real, regenerate, your box, measured, transcript.

---

## 7. Channel plan by phase

### Phase 0, days 0 to 30: foundation and proof

- Complete section 5 items 1 to 8.
- Start a build-in-public thread on X and a LinkedIn post cadence from the founder account: one post per week showing a real render, a review-loop score, or a room transcript. No stock imagery, ever.
- Record a 3-minute walkthrough of one workflow run (screen capture of the execution view) for the pilot outreach email.

### Phase 1, days 30 to 90: design partners and the first public launches

- **Direct outreach to 40 Tier 1 teams**, targeting 5 to 8 pilots. Source lists from recent Product Hunt dev-tool launches whose pages have no video, Show HN posts of React-based products, and open-source companies with a changelog. Message: "I ran ReelForge on a public repo like yours; here is the render. Want one from yours?"
- **Show HN.** Title pattern: "Show HN: ReelForge, self-hosted agents that render a promo video from your codebase". Lead with the working demo and the ReelForge-on-ReelForge video. Research says the front page hinges on a working demo people can try; without a hosted tier, offer a public sample gallery and a one-command compose setup. Expect scrutiny on the "AI" label; the never-a-timestamp and review-loop details are the answer.
- **Product Hunt.** Launch the same week. About 750 launches a day and 25% to 28% AI-tagged means the video and the mechanism story carry the page. The launch video must be the ReelForge-made one.
- **Remotion community.** Publish a technical post on how the translation agent generates Remotion compositions from component inventories and theme tokens, and how the render runs in a sandbox. Submit it to the Remotion showcase and Discord. This community has 150k agent-skill installs and is exactly the audience that understands the mechanism in one read.
- **Coding-agent communities.** A companion post for Claude Code and Cursor users: "Remotion skills get you one video in a hundred prompts; here is what a pipeline with a review loop looks like." Position as the next step, not a rival.

### Phase 2, days 90 to 180: content engine and partners

- **Comparison pages**, one per named alternative, written from the research tables: versus Arcade Creator Studio, versus Clueso, versus RepoClip, versus HeyGen and Synthesia, versus Descript and Eddie AI for Pipeline B, versus DIY Remotion plus Claude Code. Each page leads with the input difference (recording, URL, prompt, README versus source) and the deployment difference (cloud versus your box).
- **Engineering blog series** on the mechanism: why the editor never sees a timestamp; how the edit room converges; why ffmpeg runs in the engine and not in the sandbox; how overlay text is sanitized and never interpolated into a filter string. These are the thought-leadership assets for VP4 and they are already written in `docs/video-editing.md`; edit for a public audience.
- **Studio and agency partner programme** (Tier 2). Offer Pipeline B first: a self-hosted rough-cut pipeline that protects their margin. Their client videos become ReelForge proof and their relationships become distribution.
- **Case videos** from each pilot, made with ReelForge, published with the customer's score history and a short quote.
- **Release-cadence outbound** using the loss-aversion angle: pull a prospect's public changelog, count releases, count videos, send the gap.

### Phase 3, conditional on a hosted tier: self-serve

- Only if section 4 Phase 3 is built. Then: watermark-to-paid funnel from the Product Hunt and Show HN audience, a GitHub Action that renders a video on every release (RepoClip already ships this pattern and it is the most natural fit for the "regenerate" story), and integrations with changelog tools.

---

## 8. Content engine

Three pillars, each mapped to a value proposition, with formats and cadence sized for one founder
plus occasional help.

| Pillar | VP | Formats | Cadence |
|---|---|---|---|
| Compiled from the code | VP1 | Real renders with the source diff that produced them; "we changed the theme, the video changed" clips; the ReelForge-on-ReelForge video and its iterations | Weekly post, monthly long-form |
| Nothing faked | VP2 | Side-by-sides against avatar and prompt tools; a running "AI slop" scorecard the product passes by construction; disclosure and labelling guidance under EU AI Act Article 50 | Biweekly |
| Readable editorial AI | VP4 | Room transcripts annotated; review-loop score histories; engineering posts from the video-editing docs | Biweekly |

Specific pieces to produce first, in order:

1. "We made our launch video from our own repo. Here is every draft and every score."
2. "Why our video editor is not allowed to know what time it is." (never-a-timestamp)
3. "Screen recordings go stale. Source does not." (enemy framing, Challenger structure)
4. "Three colorists and a director argued about one grade. Here is the transcript."
5. "The self-hosted video pipeline: what runs where, and where your source goes."
6. "ReelForge versus RepoClip: summarizing a repo versus rendering it."
7. "What ReelForge does not do yet." (the limits page, kept current)

---

## 9. Sales motion for the sales-led phase

The product is sold, not signed up for, so the sales process is the funnel.

1. **Qualify** on the Tier 1 profile in section 3. Ask for stack, release cadence, last video spend, and whether an engineer can run Docker Compose.
2. **Show, do not pitch.** Before the call, run Quick Win Promo on a public repo of theirs or a close analogue. Open the call with their render and its score history.
3. **Deploy together.** A 60-minute screen-share to bring up the stack on their VM with their key. This is the moment the "your box" promise becomes real and it surfaces setup friction to fix in the product.
4. **First render on their real source** within a week. Iterate the workflow with them: template choice, review threshold, optional derush of any footage they have.
5. **Convert** on the pilot terms in section 4, and collect the case video, quote and permission at the same time.
6. **Expand** to Pipeline B once they have footage, and to a licence renewal at the price of one agency video.

Keep one page of call notes per pilot. The objections and language they use replace the missing
voice-of-customer quotes in the research within a month.

---

## 10. Metrics and targets

Leading indicators for the first 180 days. Revenue is not a leading indicator at this stage.

| Metric | 90-day target | 180-day target |
|---|---|---|
| Public sample renders on the site | 3 | 8 (including 5 customer case videos) |
| Design partner pilots running | 5 | 8, with 3 converted to a paid licence |
| Qualified pilot requests from the site | 15 | 40 |
| Show HN and Product Hunt: front page or top 10 of the day | 1 of 2 | Second launch for a major feature (repo import or approval gate) |
| Remotion community post: showcase inclusion and Discord discussion | 1 | 2 |
| Founder posts with a real render | 12 | 24 |
| Comparison pages live | 0 | 6 |
| Time from `docker compose up` to first render on a fresh VM (measured in pilots) | Under 2 hours | Under 45 minutes |
| Median review-loop iterations to pass threshold (product quality proxy) | Recorded | Trending down |

Instrument the site with GA4 events that already exist (`generate_lead`) and add pilot-request as
a distinct event so Tier 1 leads are counted separately from general contact.

---

## 11. Budget and effort

Assume one technical founder at roughly one day per week on go-to-market, plus a short contract for
copy editing of the comparison pages. Cash costs in the first 180 days: domain and hosting for the
marketing site, one VM for public sample renders, model spend for the sample and pilot renders, and
the Remotion Automators licence once the company passes three employees. No paid ads. The largest
cost is founder time on the eight pilots, and it is the highest-return spend available because each
pilot produces a proof asset, a pricing datapoint and a list of setup frictions to remove.

---

## 12. Risks and mitigations

| Risk | Likelihood | Mitigation |
|---|---|---|
| Remotion skills plus Claude Code make the DIY path good enough for indie founders | High | Position above DIY: pipeline, review loop, rooms, footage editing, self-hosted infra. Publish the "100 prompts versus one workflow" comparison. Target teams whose engineers' time is the scarce resource. |
| Arcade, Clueso or Storylane add a "connect your repo" input | Medium | Move first on the category name and the public proofs. Build repo import so the story is literal. Their architecture is capture-first; a retrofit will be shallow, like RepoClip's. |
| Self-hosting friction kills conversion | High | Deployment guide, requirements page, deploy-together calls, measure time-to-first-render in every pilot, consider the narrow hosted tier after ten pilots. |
| Output quality varies by codebase | High | Publish samples across three different stacks. Keep the review threshold visible. Lead with component-heavy React products where the analyzers are strongest. |
| "AI video" label triggers backlash on Show HN | Medium | Never say AI in the headline. Say compiled, rendered, measured. Lead with the never-a-timestamp mechanism and the transcript. |
| Remotion licence surprises a customer after purchase | Medium | Disclose in pricing, in the deployment guide and on the sales call. |
| Two pipelines dilute the message | Medium | Lead with Pipeline A for 90 days. Introduce Pipeline B through studios and as an add-on, under the same "real footage, measured decisions, your box" story. |
| EU AI Act and platform labelling | Low today | No synthetic presenters or voices shipped. If TTS is added, label outputs and add C2PA credentials. Publish a one-page guidance note. |
| Legal entity and terms unfinished | Certain today | Section 5 items 2 and 3 before any outreach. |

---

## 13. 90-day calendar

| Week | Actions |
|---|---|
| 1 to 2 | Wire contact webhook. Fill legal placeholders. Finish launch checklist. Run Quick Win Promo on ReelForge's own `web/` and `site/`, publish the video and score history on the homepage. |
| 3 to 4 | Two OSS sample renders with permission. Deployment guide, requirements page, limits page. Rewrite hero and features copy per section 2 and 6. Add pilot-request path. Start weekly founder posts. |
| 5 to 6 | Build the Tier 1 outreach list of 40. Pre-render analogues for the top 15. Send outreach. Book deploy-together calls. |
| 7 to 8 | Show HN and Product Hunt launch in the same week, with the ReelForge-made video. Remotion showcase and Discord post. Coding-agent community post. |
| 9 to 10 | Five pilots deployed. Collect setup frictions, ship fixes. Draft the first two engineering posts (never-a-timestamp, screen recordings go stale). |
| 11 to 12 | First pilot case video. Decide pricing hypothesis from pilot conversations. Draft the first three comparison pages. Review metrics against section 10 and decide whether repo import or the approval gate ships next. |

---

## 14. Decisions this document asks the founder to make

1. Confirm Pipeline A leads for 90 days and Pipeline B follows through studios.
2. Approve the hero rewrite and the retirement of "agentic" from headlines.
3. Approve a pilot programme with a nominal setup fee and a public case-video requirement.
4. Sequence repo import and the approval gate as the next two product items, since both are marketing prerequisites as much as features.
5. Fill the legal placeholders, which no one else can do.
