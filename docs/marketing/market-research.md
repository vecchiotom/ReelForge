# ReelForge Market Research

Compiled 2026-09-18. Product facts come from this repository. Market and competitor facts come from
web research run the same day; every external figure carries a source in the appendix. Figures that
conflict across sources are flagged. Nothing here invents company facts that the repo does not hold
(legal entity, customers, pricing), consistent with the "no invented facts" rule in
`docs/marketing-site.md`.

Companion document: [`marketing-strategy.md`](marketing-strategy.md).

---

## 0. Research Context Block

Answers to the intake interview, taken from the codebase instead of the founder.

| Question | Answer from the repo |
|---|---|
| What is the product | ReelForge is a self-hosted, agentic video platform. Pipeline A reads a software product's uploaded source tree, extracts its components, routes, dependencies and design tokens, writes Remotion React compositions in a sandbox, renders one 1920x1080 MP4, and scores it in a review loop. Pipeline B takes raw footage, runs deterministic ffmpeg analysis (silence, shots, optional Whisper transcription, per-shot colour and motion), lets LLM agents choose what to keep by opaque id only, and compiles a frame-accurate cut with optional motion graphics, music, SFX, colour grade and tracked screen inserts. |
| Main components | Visual workflow builder with conditionals, loops, parallel and review-loop steps. 20+ built-in agents in four families (analysis, translation, production, quality) plus the video-editing agents. Eleven workflow templates (`WorkflowTemplateCatalog.cs`). Multi-agent "rooms" (edit, graphics, colour grade). Bring-your-own inference provider (Azure OpenAI or any OpenAI-compatible endpoint, including local Whisper). Real-time SSE execution view with reasoning and chat transcripts. Docker Compose deployment, 16 services. |
| Ideal customer as the site implies | A technical buyer at a software company: "point ReelForge at your codebase". B2B (contact form asks for company). Must tolerate self-hosting and admin-provisioned accounts. Secondary: teams with real footage that want automated rough cuts. |
| Named competitors | None named in the repo. Identified in this research: Arcade, Clueso, Storylane, Supademo, Screen Studio (product-demo tools); HeyGen, Synthesia, InVideo, Creatify (agentic promo generators); Eddie AI, Descript, Gling, OpusClip (rough-cut editors); RepoClip, Poko Motion, Frame24, Demosmith, Motionflare (repo- or URL-to-video indies). |
| Stage | Product defined and running end to end (a March 2026 run over 108 files is captured in `docs/workflow-log.md`). No pricing, no self-serve signup, no hosted offering, no customers, no legal entity filled in. Stage (a): need everything. Module sequence 0, 1, 2, 3, 4, then 5 and the output templates. |

Hard product constraints that shape every conclusion below (from `site/`, `web/`, `api/`, `inference/`):

- Input is uploaded files, not a repo URL. There is no git or GitHub integration.
- Output is exactly one MP4 at 1920x1080, 30 fps, length set by the plan. No vertical variants, no subtitle export, no text-to-speech; the scriptwriter emits voiceover text only.
- Deployment is single-tenant Docker Compose. No billing, tenants, teams, SSO or API keys.
- The only conversion path on the marketing site is a contact form, and submissions are only logged unless `CONTACT_WEBHOOK_URL` is set.
- Every legal and company fact on the site is a bracketed placeholder.

---

## 1. Market landscape

### 1.1 Sizing

The category ReelForge sits in is small and young. The budgets it displaces are large and old.

| Layer | Size | Growth | Source, year |
|---|---|---|---|
| AI video generator software (direct category) | $0.72B to $1.23B in 2025, depending on scope | 18.8% to 46% CAGR (sources disagree) | Grand View, Fortune BI, Market.us, Intel MR, 2025 to 2026 |
| Explainer video software | $1.73B in 2025 | 15.2% CAGR to $5.36B by 2033 | Verified Market Research, 2025 |
| Explainer video production services | $3.1B in 2025 | 4.4% to 9.3% CAGR (conflict) | Intel MR 2026, WiseGuy 2025 |
| Product demo video production services | $1.17B in 2025, $1.27B in 2026 | 8.6% CAGR to $2.08B by 2032 | 360iResearch, 2026 |
| US digital video ad spend | $64B in 2024, >$80B in 2026 | About 11% per year, faster than total ad market | IAB, 2025 and 2026 |

Reading: the direct "AI video generator" bucket is under $2B and mostly avatar and text-to-clip tools.
ReelForge's real prize is the services line: roughly $4B to $5B a year that software companies and
agencies spend on explainer and demo production, at $4k to $10k per 60 seconds and 4 to 8 weeks per
video. That is the budget an agentic pipeline replaces.

Directional bottom-up for the launch-video wedge only:

| Signal | Value | Source, year |
|---|---|---|
| Product Hunt launches | About 750 to 790 per day in 2026, so roughly 270k per year | PH Launch Kit, 2026 |
| Show HN posts | 28,302 in 2025, three times the pre-2020 level | danfking, 2026 |
| AI share of PH launches | 25% to 28% | PH Launch Kit, 2026 |
| Explainer videos made by | 68% of video marketers | Wyzowl 2026 via Arcade |

Even 1% of Product Hunt launchers paying $500 for a launch video is a $1.35M annual pool from a
single channel. That is a wedge, not a market. The market is the recurring launch, feature and
release video cadence of B2B software teams.

### 1.2 Buyer behaviour

| Fact | Value | Source, year |
|---|---|---|
| B2B buyers who prefer a rep-free buying experience | 67% (Gartner) to about 75% (Consensus). Conflict, different samples | Gartner 2026, Consensus 2025 |
| Buyers who wait before first talking to sales | 6 to 10 days, sellers get 17% of buyer time | Consensus, 2025 |
| Decision-makers who prefer a short demo video to a whitepaper | 73% | Vidico compilation, 2026 |
| Companies using product demo videos at consideration stage | 57% | Komet Media, 2026 |
| Businesses using video as a marketing tool | 91% | Wyzowl 2026 |
| Marketers reporting good ROI from video | 82%, down from 93% the prior year | Wyzowl 2026 |
| Video marketers who used AI to create or edit video | 63%, up from 51% | Wyzowl 2026 |
| Non-users' top barriers | Not needed 24%, too expensive 24%, lack of time 19%, unclear ROI 10%, don't know where to start 10% | Wyzowl 2026 |

The ROI drop from 93% to 82% while adoption rose is the most useful number in this table. More
video is being made, and more of it is mediocre. The market is moving from "make video" to "make
video that does not look generated".

### 1.3 What the alternatives cost today

| Alternative | Cost | Time | Source, year |
|---|---|---|---|
| Agency 60-second explainer | $8,746 average, most pay $4k to $10k | 4 to 8 weeks; 6 to 10 weeks for animated | Vidico 2026, Yans Media 2026 |
| Mid-market studio | $5k to $15k | Weeks | Knowlify, 2026 |
| Fiverr SaaS demo gig | $100 to $600 | Days | Fiverr listings, 2026 |
| Freelance editor, per finished minute | $20 to $80, studios $75 to $200 | 1 to 2 hours of editing per finished minute | WhatShouldICharge 2025, Content Beta 2026 |
| YouTube editor retainer | $2,500 to $10,000 per month | Ongoing | Tech Packer 2025, Pixflow 2026 |
| Interactive demo platforms (Consensus, Walnut) | $20k to $50k per year | Ongoing | r/salesengineers via Substack, 2025 |
| Product-demo SaaS seat (Arcade, Clueso, Supademo) | $38 to $200 per month | Same day | Vendor pricing pages, 2026 |
| DIY with Claude Code plus Remotion skill | "About 100 prompts, not 1" for a 50-second video | Hours to days | Matt Van Horn on X, Jan 2026 |

### 1.4 Trends that matter

1. Agentic video is now a category word. HeyGen Video Agent, InVideo Agent One, Opus Agent,
   Vizard Agent, Descript Underlord, Arcade Creator Studio and Storylane Lily all shipped in the
   last 18 months. "Agentic" alone no longer differentiates.
2. The AI slop backlash is measurable. 83% of consumers have watched a video they suspected was AI.
   36% say AI video lowers brand perception. 54% trust a brand less when ads look AI-made. Human-made
   ads test 14% to 17% stronger on brand equity. Coca-Cola, McDonald's and Skechers took public hits
   in 2024 and 2025.
3. Real-product footage is the antidote buyers ask for. Arcade's own positioning has moved to
   "product-native AI video that shows the actual software" and "prospects need to see the product,
   not the spokesperson".
4. Remotion crossed from library to platform. 59.7k GitHub stars, about 170k weekly npm downloads,
   official Agent Skills with 150k installs in 8 weeks, and a company license that charges $0.01 per
   render for automated pipelines. Rendering real UI programmatically is now a mainstream idea.
5. Bring-your-own-model and data residency are procurement defaults. About 9 in 10 enterprises
   require or prefer BYO API keys. 93% are repatriating or evaluating repatriating AI workloads.
   Self-hosted ChatGPT alternative searches rose 1,400% in a year.
6. Developers distrust AI output that is "almost right". 45% name it their top frustration and only
   29% trust AI accuracy (Stack Overflow 2025). A product that shows its reasoning, scores its own
   drafts and never lets a model invent a number speaks directly to that group.
7. OpenAI shut Sora down (app April 2026, API September 2026). Generic text-to-video is
   consolidating into a few model labs and a long tail of aggregators. Workflow and grounding are
   where the remaining value sits.

### 1.5 Regulatory

EU AI Act Article 50 transparency duties applied from 2 August 2026. Providers must mark
AI-generated outputs in a machine-readable way and deployers must disclose deepfakes. YouTube, TikTok
and Meta each require disclosure for realistic synthetic media, auto-detect C2PA credentials, and
exempt AI used for scripting, ideation or editing assistance. A Remotion render of a product's real UI
and an AI-planned cut of real footage generally fall under the editing-assistance exemption. Synthetic
narrators, cloned voices and generative b-roll would not. ReelForge ships none of those today, which
is a positioning asset. Embedding C2PA credentials in renders is the cheap future-proofing step.

---

## 2. Module 0: Competitor research

ReelForge straddles three arenas that currently do not overlap. No competitor found covers more than
one of them.

### 2.1 Competitor landscape map

| Competitor | Core Promise | Unique Mechanism | Target Avatar | Soph. (1-5) | Positioning Gap |
|---|---|---|---|---|---|
| Arcade | "Empowers teams to be great storytellers"; Creator Studio makes branded product videos from a screen recording plus a prompt | Chrome or desktop capture becomes interactive demo, MP4 and social clip; AI voice "Avery" narrates | PMMs and GTM teams at B2B SaaS; 14,000+ companies | 4 | Input is a recording of the UI, never the code. Cloud only. Per-seat plus credits. |
| Clueso | Raw screen recordings into polished product videos and docs "in minutes" | Auto zoom, AI voiceover, 80+ languages; public MCP server and 90 agent skills (June 2026) | Product, CS and enablement teams | 4 | Same recording-first limit. Closest to an "agentic video" story in the demo space. |
| Storylane | "Interactive demo software, built with AI" | HTML capture; Lily agent builds demos; RepX sales agent | Sales and marketing GTM teams; 5,000+ teams | 4 | Interactive demos, not video files. |
| Supademo | "AI interactive product demos" | Capture plus AI voiceover, branching; AI Demo Agent replaces book-a-demo forms | SMB to mid-market marketing, sales, CS; 200k+ users | 4 | Interactive first, video second. |
| Screen Studio | "Professional screen recorder for macOS" | Auto zoom, smooth cursor, 4K; Mac only | Indie devs and founders | 3 | A recorder. No narrative, no analysis, no editing agent. Named repeatedly in "how do I make sleek demos" threads. |
| Guidde | Turns workflows into video tutorials "in seconds" | Capture plus generative voiceover; $80M raised | Enablement, L&D, IT at enterprise | 4 | Digital adoption, not marketing. Best funded in the demo arena. |
| HeyGen | "Create realistic AI videos of yourself in minutes"; Video Agent | Photoreal avatars, lip-sync; prompt-to-video agent and API | Creators, marketers, localization; $200M+ ARR | 4 | Avatar-centric; the thing the slop backlash targets. |
| Synthesia | "#1 AI video platform for business" | Express-2 avatars, dubbing, interactive video agents; $150M+ ARR, $4B valuation | Enterprise L&D and comms | 4 | Training and comms, not product marketing. Uncanny-valley complaints are common. |
| InVideo AI | "Turn your idea into a video with just a prompt" | Agent One: script to stock or generative clips to voiceover; $70M ARR | SMB marketers, faceless channels | 3 | Stock-montage lineage; nothing grounded in a real product. |
| Creatify | "Create winning ads with AI"; link to ad | Scrapes a product URL into a script and avatar ad; AdMax ad agent | DTC performance marketers | 4 | URL scraping is the nearest "product-aware" input; still avatar output. |
| Descript | "Edit video like a doc" | Transcript editing; Underlord agent does rough cut, B-roll, captions from one prompt | Podcasters, YouTubers, marketing video teams; $55M ARR | 4 | Text-anchored, single agent, cloud, credit-metered (users complain credits last "about a day"). |
| Eddie AI | "The assistant video editor for pros"; story-driven rough cuts from raw footage | Logs footage, syncs multicam, builds rough cut, exports an NLE timeline; MCP server; pay as you go credits | Professional editors, doc and podcast post houses | 4 | Closest analogue to Pipeline B. Outputs a timeline for a human to finish, not a render. Single agent. Desktop app. |
| Gling | "Auto remove silences and bad takes" | Transcribe then cut silence, fillers, repeated takes | Talking-head YouTubers | 3 | Single-speaker only; no story judgement. |
| OpusClip | "#1 AI clipping tool to create viral shorts" | ClipAnything, Agent Opus; 6M+ users | Creators, media | 3 | Repurposing, not assembly. Auto-cut pacing complaints ("every 3 to 5 seconds"). |
| RepoClip | Promo video from a GitHub repo URL; GitHub Action on release | Gemini summarizes the repo into a script, Flux images, TTS, Remotion Lambda render | Indie devs; Show HN 2026 | 3 | Repo in, Remotion out, but the output is a description with generated imagery. Does not render the product's real components. |
| Poko Motion | Repo, README, deck or URL into a motion demo | Script plus scenes, renders locally, chat editing | Indie founders; LTD marketplaces | 3 | README-driven, not code-driven. |
| Frame24 | Original 30-second films from a URL, "no templates, avatars" | Reads positioning, colours, typography from the page | Founders | 4 | Closest aesthetic promise. URL in, not code in. |
| Demosmith | Autonomous agent drives your live app and produces a demo MP4 in about 10 minutes | Browser agent captures, scripts, narrates | SaaS founders; from $40/mo | 4 | Drives the running app, not the code. |

Expanded notes on the gaps that matter most:

**Arcade** is the benchmark for what a well-funded, recording-first product-video tool looks like, and
Creator Studio (February 2026) is the closest output overlap: on-brand narrated product marketing
video with motion graphics and multi-format export. Its ceiling is its input. A recording captures
whatever a human clicked through, at whatever resolution and state the app happened to be in. It
cannot render a component that was not on screen, restyle it from the design tokens, or regenerate
the video when the UI changes without someone re-recording.

**Clueso** matters because it is the first demo-video vendor to expose itself as an MCP server with
agent skills, so "an agent makes the video" is now a claim a Level-4 buyer has already heard. ReelForge
cannot win on "agentic" alone.

**Eddie AI** is the competitor to study for Pipeline B. It has already educated professional editors
that an assistant can log, sync and rough-cut raw footage overnight. It stops at a timeline, keeps a
human in the NLE, and is single-agent. ReelForge finishes the render and adds deliberation, but it is
also weaker on the things editors expect: multicam, EDL export, reordering.

**RepoClip** proves the "repo to video" search intent exists and has a working, cheap, Remotion-Lambda
product behind it. Anyone Googling "generate video from GitHub repo" finds it first. It also proves
the ceiling of the shallow version: a summary of the README illustrated with generated stills.

**Descript** owns the "edit by transcript" mental model, and its Underlord agent already makes rough
cuts from a prompt. Its credit metering is the most-complained-about thing in its reviews, which
opens a self-hosted, own-model, no-credits angle.

Saturated claims the market has already heard too often:

1. "Create professional videos in minutes."
2. "Just describe it and AI makes the video" (prompt-to-video).
3. "Realistic AI avatars / AI voiceover in 80+ languages."
4. "Turn your screen recording into a polished demo."

### 2.2 Competitive gap analysis

**Overcrowded angles**

- Prompt-to-video and idea-to-video agents (HeyGen, InVideo, Vizard, Opus, Kapwing).
- Screen recording plus auto-zoom plus AI voice (Arcade, Clueso, Guidde, Tella, Supademo).
- AI avatars and UGC actors (Synthesia, HeyGen, Arcads, Creatify).
- Auto-remove silences and filler words (Descript, Gling, Wisecut, AutoPod, Loom).
- "Minutes not weeks" speed claims from every vendor above.

**Underserved desires**

- A video generated from the product's own source, so it is always faithful to the real UI and can be regenerated on every release.
- Video that does not look generated. Buyers now ask for "the product, not the spokesperson".
- Control over where footage and code go. No demo tool offers self-hosting; no rough-cut tool except Eddie keeps footage local.
- A model choice the buyer controls and pays for directly, without vendor credits.
- Editorial judgement that can be audited: why was this shot kept, why that overlay placed.
- One pipeline for both the generated promo and the edited real footage.

**Open positioning territories**

1. *Source-native product video.* The video is compiled from the code, the way a build is compiled
   from the code. Nobody occupies this. RepoClip and Poko are adjacent but summarize rather than
   render. The claim is defensible because ReelForge literally writes and renders Remotion components
   derived from the project's own components and theme tokens.
2. *Private video infrastructure.* Docker Compose, own Postgres, own object store, own model
   endpoints, own Whisper. Every competitor in all three arenas is cloud SaaS. For regulated,
   security-conscious or cost-sensitive engineering teams this is the whole decision.
3. *Auditable editorial AI.* Agents choose from offered ids only, never emit a timestamp, and rooms
   leave a transcript. This turns the "almost right" fear developers have about AI into a
   verifiable process. No editing competitor talks about how the decision is constrained.

**Most underserved sophistication level**

Level 4. The buyers who matter have already tried an avatar tool or a text-to-video generator and
got something they were embarrassed to publish, or paid an agency and waited eight weeks for a
video that was out of date at delivery. They do not need a bigger promise. They need a different
mechanism that explains why the output will be faithful and why the process will not need
babysitting. Level 5 identity plays ("become a video-first company") are premature for a product
with no customers.

### 2.3 Differentiation pressure test

| Dimension | Competitors (consensus) | ReelForge (potential) |
|---|---|---|
| Input | Screen recording, URL, prompt, or README | The source tree itself, plus assets |
| Fidelity to the real product | As good as the recording | Rendered from the product's own components and design tokens |
| Regeneration on change | Re-record | Re-run the workflow |
| Output style | Templates, avatars, stock, or zoomed recording | Programmatic Remotion composition, no templates |
| Quality control | Human review | Review agent scores and loops drafts; humans review the transcript |
| Editing of real footage | Transcript-anchored, single agent, cloud | ffmpeg analysis, id-anchored decisions, multi-agent rooms, self-hosted |
| Where data lives | Vendor cloud | Customer's Postgres, MinIO and model endpoint |
| Pricing model | Seats plus credits | Undefined today; self-hosted removes vendor credits entirely |
| Deployment | SaaS | Docker Compose |
| Time to first video | Minutes | Hours of setup, then minutes per run |

Does the product sound like anyone else? Yes, on the surface. "AI agents turn your codebase into
promotional videos" reads as one more prompt-to-video claim to a jaded scroller, and "agentic
workflow engine" sounds like every 2026 vendor. Where it is genuinely different is the input, the
render, and the deployment: the only product that compiles a promo from the code that ships, renders
it as real React, and runs entirely inside the customer's infrastructure. What stops a Level-4 buyer
mid-scroll is a side-by-side: the app's actual signup screen rendered from its actual component next
to a HeyGen avatar describing it, with the line "no recording, no template, no avatar, regenerated on
every release".

### 2.4 Competitor comparison table

| Competitor | Core Claim | Unique Mechanism | Soph. | Gap or Weakness | How We Are Different |
|---|---|---|---|---|---|
| Arcade | Storytelling from a recording; Creator Studio promo video | Capture plus AI narration and motion | 4 | Recording-bound, cloud, per seat plus credits | Code-bound, self-hosted, regenerates on change |
| Clueso | Recording to polished video and docs in minutes | Auto zoom, voice, MCP and skills | 4 | Recording-bound, credits | Same; plus review loop and real footage editing |
| HeyGen | Realistic AI videos of yourself | Avatars, Video Agent | 4 | Avatar slop backlash, no product grounding | Shows the product, never a presenter |
| Synthesia | #1 AI video for business | Avatars at enterprise scale | 4 | Uncanny valley, L&D focus | Same |
| InVideo AI | Idea to video with a prompt | Agent One, stock and generative clips | 3 | Nothing real in the frame | Real UI, real footage only |
| Creatify | Link to winning ad | URL scrape plus avatars | 4 | Ad-only, avatars | Source-native, self-hosted |
| Descript | Edit video like a doc | Underlord agent, transcript editing | 4 | Credits, single agent, cloud | Own model, no credits, rooms, audit trail |
| Eddie AI | Assistant editor for pros | Rough cut plus NLE export, MCP | 4 | Timeline only, desktop, single agent | Finished render plus graphics, music, grade; multi-agent |
| Gling | Remove silences and bad takes | Transcript-based auto cut | 3 | Talking head only | Shot, silence and transcript together, id-anchored |
| RepoClip | Promo from a GitHub repo | Gemini summary, generated stills, Remotion Lambda | 3 | Describes the repo, does not render it | Renders the actual components |
| Frame24 | Original film from a URL | Reads brand from the page | 4 | URL in, no editing pipeline | Code in, review loop, editing pipeline |
| Demosmith | Agent drives your app into a demo | Browser automation | 4 | Needs a running app and creds; cloud | Works from source; self-hosted |
| **ReelForge** | Promo video compiled from your codebase; real footage edited by deliberating agents; all in your own infra | Source-native Remotion render; id-anchored, never-a-timestamp editing; review loop; rooms | 4 | No self-serve, no hosted tier, upload not git, one MP4 format, no voice | The only product in all three arenas at once, and the only self-hosted one in any of them |

---

## 3. Module 1: Product

### 3.1 Features, benefits, dimensionalized

| Feature | Problem Solved | Benefit | Dimensionalized Benefit |
|---|---|---|---|
| Codebase analysis agents (structure, dependencies, components, routes, style tokens) | Video makers do not understand the product; founders do not have time to brief them | The video knows the product as well as the engineers do | You drop the `src/` folder in on Tuesday night. By the time you refill your coffee the agents have inventoried 108 files, named every route, and pulled the exact violet from your theme file. The script mentions the feature you shipped yesterday because it read the component. |
| Remotion translation and render | Recordings look like recordings; templates look like templates | Every frame is your real UI, drawn from your real components, at any resolution | The dashboard in the video is your dashboard, rendered as React at 1080p with the empty state you never manage to screenshot cleanly. Change the primary colour next sprint, re-run, and the video changes with it. |
| Director, Scriptwriter, Author agents | Nobody on the team is a video director | A narrative, a script and a shot list without hiring anyone | You get a 12-scene plan with a hook, a problem, three feature beats and a CTA, written for your audience, with voiceover text you can hand to whoever records it. |
| Review loop with score threshold | First drafts from AI are "almost right" | Drafts below the bar never reach you | The review agent scores draft one at 6, sends it back with concrete notes, and you see draft two at 8. You only look at the one that passed. |
| Visual workflow builder (conditionals, loops, parallel, extract) | Every team's process is different; black-box tools cannot be adapted | Change the pipeline without changing the code | Marketing wants a 20-second cut for LinkedIn and a 60-second cut for the site. You clone the template, drop a parallel step, and both render from the same analysis. |
| Video derush pipeline (silence, shots, ASR, compile) | Hours of raw demo or interview footage sit unedited because editing costs $20 to $80 a finished minute | A tight first cut without an editor, in your own infrastructure | Forty minutes of a founder walking through the product becomes a four-minute cut with the dead air gone, the good takes kept, and the transcript intact. |
| Id-anchored decisions ("never a raw timestamp") | Models hallucinate numbers and cut mid-word | Every cut lands on a frame the analysis measured | The story editor says "keep t3, t7, s12". It cannot say 00:41.3. The compile step resolves those ids to frame-exact boundaries the silence detector found, so no sentence is ever cut in half. |
| Rooms (edit, graphics, colour grade) with transcripts | A single model's taste is arbitrary and unexplainable | Multiple perspectives argue, a director decides, and you can read why | The continuity artist objects to the warm grade because shot 4 is a screen capture. The director agrees and picks Muted. You read the exchange in the artifact and stop wondering whether the AI just guessed. |
| Motion graphics, music, SFX, colour grade, tracked inserts | Post-production is a second hire | Finishing steps run in the same encode | The lower-third appears in the safe zone the analyzer measured, music ducks under speech, a whoosh lands on the cut, the grade is one consistent look, and the phone in the founder's hand shows a rendered UI tracked frame by frame. |
| Bring your own inference provider, local Whisper | Vendor credits, vendor models, vendor data policies | You pick the model and pay the model vendor directly | Route the scriptwriter to your Azure OpenAI deployment in Switzerland, the colorist to a cheap OpenAI-compatible endpoint, and transcription to the Whisper container on the same box. No credit meter. |
| Self-hosted Docker Compose, own Postgres and object store | Source code and raw footage cannot leave the building | Security review is a `docker compose up`, not a vendor questionnaire | Your security lead asks where the source goes. The answer is "the MinIO bucket on this VM". |
| Real-time execution view with reasoning and chat transcripts | AI tools are black boxes | You watch the agents work and can stop trusting on faith | Every tool call, every draft, every room turn streams to the screen while it runs. |

### 3.2 Unique mechanism, step by step

**Source analysis.** Problem: video makers work from a brief and a recording, so the video drifts from
the product. Action: five analysis agents read the uploaded tree in parallel, producing a structure
map, dependency list, component inventory with props, route map and design-token sheet. Outcome: a
ground-truth model of the product that no recording contains.

**Programmatic render.** Problem: templates and avatars look generated; recordings look recorded.
Action: a translator agent writes Remotion React components that recreate the app's screens from the
inventory and tokens; an animation agent sets timing; the author builds, typechecks and renders in an
isolated sandbox. Outcome: a video whose every pixel is derived from the codebase and can be
regenerated when the code changes.

**Scored iteration.** Problem: AI output is almost right. Action: a review agent scores the render
against a threshold and loops the production stage with structured feedback up to a set number of
times. Outcome: the human sees only drafts that passed.

**Measured footage.** Problem: editing real footage needs an editor's hours. Action: ffmpeg detects
silence gaps and shots, Whisper transcribes, a pure C# analyzer measures motion, exposure, colour and
safe zones per shot, and everything is offered to the model as opaque ids. Outcome: a bounded,
factual view of the footage that a model cannot misquote.

**Constrained decision.** Problem: models invent timestamps and cut mid-sentence. Action: the story
editor, or an edit room of several editors and a director, chooses which ids to keep; the output
schema has no numeric fields at all, enforced by tests. Outcome: frame-accurate cuts, always on a
measured boundary.

**Deterministic finish.** Problem: graphics, music, grade and SFX usually mean a second pass in an
NLE. Action: planners choose placements, tracks, looks and cues by word and id only; the compile step
resolves them to filter parameters from first-party tables and applies them in one encode. Outcome:
a finished MP4 with post-production done and every parameter traceable.

### 3.3 Naming the unique mechanism

| Theme | Name Options |
|---|---|
| Compiled from source | Source-Native Rendering; Code-to-Frame Pipeline; Compiled Video; Build-to-Video; Source-Truth Rendering |
| Faithfulness and grounding | Real-Component Rendering; Ground-Truth Video; Product-Faithful Render; Zero-Template Rendering; Never-a-Timestamp Editing |
| Deliberation and review | The Review Loop; The Edit Room; Scored Drafting; Director-Synthesized Decisions; Multi-Seat Editorial |
| Forge metaphor (brand-aligned) | The Forge Pipeline; Forge Loop; Cast-from-Code; Forge Rooms; Source Forge |

Chosen: **Source-Native Rendering** for Pipeline A, with **Never-a-Timestamp Editing** as the
sub-mechanism name for Pipeline B and **The Edit Room** as the consumer-facing name for the
deliberation feature. "Source-native" reads immediately to an engineer, tolerates a one-line
explanation for a marketer, and no competitor uses it.

### 3.4 Mechanism with story

How it works: Source-Native Rendering treats a promo video the way a build system treats a binary.
The source tree is the input. Analysis agents compile it into a ground-truth model of components,
routes and design tokens. Translation agents turn that model into Remotion React compositions that
recreate the actual screens. Production agents script and direct a narrative around them, the author
renders it frame by frame, and a review agent refuses any draft under the score threshold. Change the
code, re-run the build, get a new video.

Before, during, after: A three-person dev-tool team ships a new query builder on Thursday and wants a
launch video for a Tuesday Product Hunt launch. Before, they would have recorded a Loom, watched it
twice, decided it looked like a Loom, and either paid a Fiverr freelancer $300 for something generic
or skipped the video. During, one of them drops the `src/` folder into a ReelForge project, picks the
Quick Win Promo template, and watches the analysis step name the query builder component and pull
the brand purple. Forty minutes and two review iterations later there is a 1080p MP4 in the outputs
panel. After, the Product Hunt page has a video that shows the real query builder, the founder
records the voiceover text over lunch, and when the UI changes in November they re-run the same
workflow instead of re-recording anything.

---

## 4. Module 2: Avatar

Primary avatar: the engineering-led founder or technical product marketer at a small B2B software
company that ships often and has no video person. Secondary avatar, sketched at the end: the small
production studio or agency lead drowning in raw footage.

### 4.1 Primary desires (6)

1. **To be seen as a real company.** A launch with a proper video signals that this is a product,
   not a side project. The desire is legitimacy in front of Hacker News, Product Hunt and the first
   enterprise prospect.
2. **To ship marketing at the speed they ship code.** They resent that a feature takes three days to
   build and three weeks to get a video for. They want the video to be a build artifact.
3. **To stay in control of quality.** They cannot stand output that is almost right. They want to
   see the reasoning, set the bar, and reject what fails it.
4. **To never look like AI slop.** They have seen the Coca-Cola reaction. Their brand is credibility
   with developers. One uncanny avatar would cost more than it saves.
5. **To keep the source and the footage in the building.** Uploading the repo to a vendor's cloud is
   a conversation with the security lead or the customer they do not want to have.
6. **To own their tools.** They chose self-hosted Postgres, their own model endpoints and Docker.
   They want a video pipeline they can inspect, fork and run, the way they run everything else.

### 4.2 Primary problems (6)

1. **The launch video is always the last thing and always late.** Every release plan ends with
   "record a demo" and every release ships without one, or with a Loom recorded at 1 a.m.
2. **Agencies cost $5k to $10k and take six weeks, and the product has changed by delivery.** The
   quote is affordable once. The cadence is not.
3. **AI video tools produce something embarrassing.** They tried a prompt-to-video tool, got stock
   footage and a synthetic voice, and never showed anyone.
4. **Screen recordings look like screen recordings.** Screen Studio helps, but it still needs a
   human to click through a tidy demo account with clean data, and the result is a recording.
5. **Raw footage piles up.** They recorded the webinar, the customer call, the conference talk.
   Editing costs $20 to $80 a finished minute or a weekend they do not have.
6. **Fear of being fooled by their own tooling.** The AI cut a sentence in half, invented a
   feature, or picked a shot for no reason they can see. They do not trust output they cannot audit.

### 4.3 Primary conflict

They want marketing that keeps pace with engineering, and the only tools that move at that speed
produce video they would be ashamed to publish. They have tried the Loom, the Fiverr gig, the
avatar tool and one agency quote. Each solved either speed or quality, never both, and none of them
knew what the product actually did. The internal monologue sounds like: "I could do it myself with
Remotion and a hundred prompts, but then I have not shipped code for two days, and I still will not
trust the cut." So the video keeps sliding to the next release.

### 4.4 Master avatar

| Category | Emotional Detail | Logical or Practical Detail |
|---|---|---|
| Hell (without product) | Launching with a screenshot and an apology. Watching a competitor's slick video get the upvotes. Deleting the avatar video before anyone saw it. Feeling like a hobby project. | Video slips every release. $5k quotes, 6-week timelines, out-of-date deliverables. Hours lost clicking through demo accounts. Raw footage unedited on a drive. |
| Heaven (with product) | Launch day feels like a real company. The video shows the actual product and looks designed. Confidence to post it on HN. Marketing that finally keeps up. | A video per release, regenerated from source. Voiceover text ready to record. Rough cuts from footage overnight. Source and footage never leave their VM. Model bill paid to their own provider. |
| Pains | Embarrassment about generated-looking output. Distrust of black-box AI. Resentment of vendor credit meters. Dread of the security questionnaire. | Cost per video, time per video, drift between video and product. Cloud-only tools. Per-seat pricing for a two-person team. Lack of an audit trail on AI decisions. |
| Gains | Pride in a video that looks like the product. Control over the bar. Being the team that automated marketing too. | Repeatable pipeline. Adjustable workflow. Choice of model. One MP4, 1080p, in the outputs panel. Transcript of every decision. |
| See | Slick launch videos from funded competitors. HeyGen ads everywhere. "I don't edit videos anymore, I prompt them" posts. Show HN threads asking how to make sleek demos. Coca-Cola AI ad backlash. | Arcade at $42.50 a seat plus credits. Remotion skills at 150k installs. Descript reviews complaining credits last a day. RepoClip on Show HN. EU AI Act labelling news. |
| Say | "It looks like a Loom." "That avatar is creepy." "Agencies charge $5k for 60 seconds." "It took 100 prompts, not 1." "Where does my source code go?" | "We need a video for the launch." "Can we regenerate it when the UI changes?" "Does it run on our box?" "Which model does it use?" "Show me why it cut there." |
| Hear | Cofounder: "we can't launch without a video again." Investor: "your marketing does not match your product." Security lead: "no third-party gets the repo." Inner voice: "you could build this yourself, and you never will." | Advisors: "video gets 2.7x the upvotes." Peers: "just use Screen Studio." Vendors: "minutes not weeks." Prospects: "send me a demo video before we talk." |
| Do | Record a Loom at midnight and not post it. Open Remotion docs and close them. Sign up for an avatar tool and cancel. Ask a freelancer for a quote and ghost them. | Skip the video. Ship the release. Push the footage to a drive. Add "launch video" to the next sprint again. |

### 4.5 Secondary avatar sketch: the production studio or agency lead

A two-to-eight-person studio that produces demo, launch and interview videos for software clients.
Desires: margin, throughput, and a way to say yes to more clients without hiring editors. Problems:
editors cost $2,500 to $10,000 a month on retainer, rough cuts eat the first two days of every job,
clients keep changing the product mid-edit. They already own the footage and the client
relationships, so a self-hosted pipeline that turns rushes into a finished first cut and regenerates
product screens from source is margin, not magic. Sophistication level 4; they have tried Descript
and Eddie. This avatar is the second sales target and the best channel partner.

---

## 5. Module 3: Market

### 5.1 State of awareness

| Awareness Stage | Emotional State | Logical State | What Moves Them Forward |
|---|---|---|---|
| Unaware | "We ship code, not videos. Marketing is a later problem." "Nobody watches those anyway." | "A README and screenshots are enough for a dev tool." "Video is for consumer apps." | Seeing a peer's launch with a real product video get the upvotes and the signups theirs did not. |
| Problem-Aware | "Our launch looked amateur again." "I hate how the Loom sounds." "We lost that deal because they never saw it working." | "73% of buyers prefer a demo video to a whitepaper, and we have none." "The video is late every release because nobody owns it." | Naming the cost: $5k and six weeks per agency video, or a founder weekend, per release, forever. |
| Solution-Aware | "The AI tools make slop." "Agencies are slow." "Screen Studio still needs me to click through it." "Maybe Remotion plus Claude Code, if I had two days." | "Recording-based tools capture what I click, not what the product is." "Prompt tools do not know my product." "I would need to keep re-recording on every change." | Learning that a video can be compiled from the source tree the way a build is, and regenerated on every release. |
| Product-Aware | "Sounds like another agent pitch." "Self-hosted is a lot of setup." "Does it actually look good?" | "Uploaded files, not git. One MP4. No voiceover audio. Docker Compose with 16 services." "Bring my own Azure OpenAI." | A side-by-side of their own app rendered by ReelForge next to what they have today, plus the review-loop transcript. |
| Most-Aware | "I want this running on our box before the next release." "I want the derush too." | "What does it cost, what is the setup time, who supports it, what about the Remotion license?" | A clear offer: pilot on their repo, a deployment checklist, a price, and a named person to call. |

### 5.2 State of sophistication

| State | Description | 3 Claims That Land |
|---|---|---|
| 1. First exposure | Simple, direct, bold | "Make a promo video from your code." "No camera, no editor." "One MP4, ready to post." |
| 2. Early awareness | Dramatized, bigger | "A launch video for every release, not one a year." "Your whole product, on screen, without recording a thing." "Video that updates when your UI does." |
| 3. Jaded | New mechanism they have not seen | "Source-Native Rendering: agents read your components and render them as React, frame by frame." "The editor never sees a timestamp, only measured ids." "Three artists and a director argue the cut, then you read the transcript." |
| 4. Burned | Faster, easier, more certain; solves what past tools missed | "No avatars, no stock, no templates: only your real UI and your real footage." "No credit meter: your model, your endpoint, your bill." "It runs on your VM. Your source never leaves." |
| 5. Checked out | New identity, not a better version | "Marketing as a build artifact." "The team whose video ships with the code." "Post-production as infrastructure." |

Primary sophistication target: Level 4. The product's strongest, most defensible facts (real components,
constrained decisions, self-hosting, own model) are all answers to a burned buyer's objections. Level
3 mechanism language is the vehicle for those answers, so Level 3 and 4 copy run together. Level 5
identity claims should wait until there are customers who can embody them.

---

## 6. Module 4: Value propositions

**VP1. The video compiles from the code.**
Every promo tool on the market starts from a recording, a URL or a prompt, so every one of them
produces a video that is a step removed from the product and out of date the moment the UI changes.
ReelForge starts from the source tree. Analysis agents inventory your components, routes and design
tokens; translation agents recreate your actual screens as Remotion React compositions; production
agents script and direct the story; a review agent scores each draft and sends back anything under
the bar. The result is a 1080p MP4 in which every frame is your product, drawn from your product,
and when you ship the next release you re-run the workflow instead of re-recording anything. It is
Source-Native Rendering, and it treats marketing video the way you already treat a build: as an
output of the code, not a project alongside it.

**VP2. Nothing generated, nothing faked, nothing you would be embarrassed to post.**
Buyers have watched enough AI video to spot it in three seconds, and 54% of them trust a brand less
when they do. ReelForge never puts an avatar, a stock clip or a synthetic presenter in your frame.
Pipeline A renders your real UI from your real components. Pipeline B edits your real footage:
ffmpeg measures every silence gap and shot boundary, Whisper transcribes the speech, and the story
editor chooses what to keep by id alone, never by a number it could get wrong, so no sentence is ever
cut in half and no shot is chosen without a measured reason. What you publish is your product and
your people, tightened by a process you can audit, on a platform that never had the option to
hallucinate a scene.

**VP3. Your source, your footage, your model, your box.**
Source code and raw footage are the two things a software company least wants in a vendor's cloud,
and every competitor in this market is a vendor's cloud with a credit meter. ReelForge is a Docker
Compose stack: Postgres, MinIO, RabbitMQ, a local Whisper container and the agents, on your VM or in
your VPC. You register your own Azure OpenAI or any OpenAI-compatible endpoint, override it per
agent if you like, and pay the model vendor directly at cost. The security review is a compose file,
the data-residency answer is "the bucket on this machine", and there is no plan tier standing
between a two-person team and the whole pipeline.

**VP4. An editorial process you can read, not a black box you have to trust.**
Developers' top complaint about AI is output that is almost right, and video is the worst place to
discover it. ReelForge is built so that every decision is bounded and every decision is visible.
Agents can only choose from ids the analysis actually offered; the output schemas have no numeric
fields, enforced by tests; overlays land only in safe zones the analyzer measured; the colour grade is
a word resolved to parameters by first-party tables. When a decision needs judgement, an edit room of
several editors and a director argue it out and the transcript is saved as an artifact. You watch the
execution stream live, you read why shot four was kept, and you set the score a draft must reach
before you ever see it.

| VP# | Name | Core Hook | Best Used For |
|---|---|---|---|
| 1 | Compiled from the code | The video is a build artifact of your source tree | Homepage hero |
| 2 | Nothing faked | Real UI, real footage, no avatars, no stock, no slop | Product page, comparison pages |
| 3 | Your box, your model | Self-hosted, BYO endpoint, no credit meter | Sales deck, security-conscious buyers |
| 4 | Readable editorial AI | Id-anchored decisions, rooms with transcripts, review loop | Thought leadership, engineering blog |

---

## 7. Module 5: Mental models

| Mental Model | Reframed Angle | Copy or Messaging Example |
|---|---|---|
| Loss Aversion | Every release without a video is a launch that under-performed and cannot be re-run | "You shipped 14 releases this year. How many had a video? The other 12 launched at half volume." |
| Enemy Framing | The enemy is the recording, not the team. Recordings capture a moment; products are code | "Stop recording your product. Start compiling it." |
| Jobs To Be Done | The job is not "make a video". It is "make the launch feel real without stopping engineering" | "Hire ReelForge to ship the launch video with the release, so nobody stays up recording a Loom." |
| First Principles | A product video is a function of the product's state. The product's state is its code. So the video should be a function of the code | "A video is a render of the product at a point in time. The product is the code. Render from the code." |
| Inversion | What guarantees an embarrassing video: an avatar, stock footage, a template, and a model that invents timestamps. Remove all four | "We removed everything that makes AI video look like AI video. What is left is your product." |
| Challenger Sale | Teach that recording-based tools have a structural ceiling, then present source-native as the next step | "Every demo tool starts from a screen recording. That is why every demo video goes stale. Here is the alternative." |

Top 3 priority angles:

1. **Enemy Framing, "stop recording, start compiling"**: homepage hero and the launch post on
   Show HN, because it names a mechanism difference in five words.
2. **Inversion, "we removed everything that makes AI video look like AI video"**: product page and
   comparison pages against HeyGen, Synthesia and InVideo, because it turns the slop backlash into
   a checklist ReelForge passes by design.
3. **Loss Aversion, "14 releases, 2 videos"**: outbound and sales deck for teams with a visible
   release cadence, because it quantifies the cost of the status quo in the buyer's own numbers.

---

## 8. Output templates

### 8.1 Avatar profile

**Name:** Marco, technical cofounder and de facto marketer at a 4-person B2B dev-tool startup.

**One sentence:** Ships every two weeks, has never shipped a launch video on time, and would rather
run a pipeline than hire an agency.

**Top 3 desires:** marketing that keeps pace with engineering; output he is proud to post on Hacker
News; tools he can inspect and run himself.

**Top 3 problems:** agencies cost $5k to $10k and six weeks per video; AI tools produce slop he will
not publish; source and footage cannot go to a vendor cloud.

**Primary conflict:** He wants speed and quality and has only ever been offered one at a time, and
the DIY route (Remotion plus a hundred prompts) costs the two engineering days he cannot spare.

**Heaven:** a video per release regenerated from source; a rough cut of the webinar by morning; the
security review is a compose file.

**Hell:** launching with screenshots again; a deleted avatar video; footage rotting on a drive.

**Language patterns:** "it looks like a Loom"; "that avatar is creepy"; "agencies charge $5k for 60
seconds"; "it took 100 prompts, not 1"; "where does my source go"; "show me why it cut there"; "can
we regenerate it when the UI changes".

**Awareness level:** solution-aware. **Sophistication level:** 4.

### 8.2 Positioning canvas

**Product:** ReelForge.

**Unique mechanism:** Source-Native Rendering. Agents compile a promotional video from the product's
own source tree and render its real components with Remotion, scored by a review loop; a companion
Never-a-Timestamp editing pipeline cuts real footage by measured ids only.

**Primary competitive gap:** every product-video tool starts from a recording, URL or prompt and runs
in a vendor cloud; none starts from the code, and none is self-hosted.

**Sophistication target:** Level 4, burned buyers who have tried avatar or prompt tools and agencies,
reached with Level 3 mechanism language.

**Four value propositions, one line each:**
1. The video compiles from the code and regenerates on every release.
2. Nothing faked: real UI, real footage, no avatars, no stock, no templates.
3. Self-hosted, bring your own model, no credit meter, source never leaves your box.
4. Editorial decisions are bounded by design and readable in a transcript.

**Top 3 copy angles:** "Stop recording your product. Start compiling it." / "We removed everything
that makes AI video look like AI video." / "14 releases this year. How many had a video?"

**Ownable sentence no competitor is saying:** "ReelForge renders your promo video from your source
code, on your own infrastructure, and never lets a model invent a frame."

---

## 9. Constraints and evidence gaps

Product claims the code does not yet support (do not market until built):

| Claim to avoid | Why |
|---|---|
| "Connect your repo" or GitHub integration | Input is drag-and-drop upload only |
| Voiceover, narration, AI voice | Scriptwriter emits text; no TTS exists |
| Vertical, square or multi-format export | One 1920x1080 MP4 per run |
| Self-serve signup, free trial, pricing tiers | Admin-provisioned accounts; no billing |
| Hosted or cloud offering, SLA, SSO, teams | Single-tenant Docker Compose only |
| Multicam, reordering, speed ramps, subtitles burn-in, EDL export | Listed as not built in `docs/video-editing.md` |
| Human approval gate mid-run | Not built; flagged as highest-value phase-2 item |

Commercial facts to resolve before any pricing conversation:

- Remotion's company license applies to for-profit users with four or more employees. Automated
  pipelines fall under "Remotion for Automators" at $0.01 per render with a $100 per month minimum.
  A self-hosting customer above three employees, and ReelForge itself above three employees, needs
  this. It must be in the offer.
- The legal placeholders in `site/lib/legal-placeholders.ts` are unfilled. The contact page renders
  a literal `[RESPONSE_TIME_SLA]` today.

Research caveats:

- Competitor homepage copy was read from search snippets because direct fetches were blocked;
  verify verbatim headlines before quoting them in comparison pages.
- Third-party ARR figures (Latka, Sacra, Tracxn) are estimates and conflict for Storylane and Clueso.
- Voice-of-customer quotes are fewer than intended because Reddit, HN and Indie Hackers pages were
  blocked. Run 10 founder interviews to replace them.
- Market-size sources disagree by a factor of two on the direct category. Use the services lines,
  which are more stable, as the sizing anchor.

---

## Appendix: sources

Market sizing: Grand View Research, AI video generator market (2026); Fortune Business Insights
(2026); Market.us (2025); Intel Market Research (2026); Verified Market Research, explainer video
software (2025); Intel Market Research, explainer services (2026); WiseGuy Reports (2025);
360iResearch, product demo video production (2026); IAB video ad spend reports (2025, 2026).

Buyer behaviour: Gartner sales survey press releases (June 2025, March 2026); Consensus B2B Buyer
Behavior Report (2025, 2026); Wyzowl State of Video Marketing 2026 via Piktochart, SociallyIn,
Pixel8, Wix, Arcade; Vidico B2B video statistics (2026); Komet Media (2026).

Costs: Vidico explainer video cost (2026); Yans Media (2026); Knowlify (2026); Videokrtoon,
TruScribe (2025); Fiverr listings (2026); GigRadar, Jobbers (2026); WhatShouldICharge (2025);
Content Beta, Krock, Moonb, Pixflow (2026); Tech Packer Substack (2025); Mihai S. Substack on
interactive demo pricing (2025).

Channels: PH Launch Kit trends (2026); Vibrantsnap PH video study (2025); Flowjam (2025); Dan F.
King, Show HN by the numbers (2026); Sturdy Statistics (2025); Voicepo subreddit sizes (2025);
Vibe Content Creation (2026); ngram.com and StartupHub on Remotion skills (2026).

Competitors: vendor pricing and blog pages for Arcade, Clueso, Storylane, Navattic, Supademo, Screen
Studio, Guidde, Tella, Jitter, Rotato, Sendspark, Loom, Vidyard, Floik, Synthesia, HeyGen, InVideo,
Pictory, Creatify, Arcads, Lumen5, Canva, Adobe Firefly and Premiere, Runway, Google Veo/Flow,
OpenAI Sora, Kling, Luma, Descript, OpusClip, Captions/Mirage, VEED, Kapwing, Eddie AI, AutoPod,
Gling, CapCut, Filmora, Wisecut, Vizard, Riverside; funding via TechCrunch, CNBC, Fortune,
BusinessWire, PRNewswire, Calcalist, Dealroom, Sacra, GetLatka, Tracxn, PitchBook (2024 to 2026);
RepoClip Show HN thread and pricing (2026); Poko Motion blog (2026); Frame24, Demosmith, Motionflare
sites (2026); Remotion license pricing, Lambda cost example, Agent Skills docs, GitHub repository
(fetched 2026-09-18).

Trends and regulation: Animoto State of Video via BusinessWire (2026); Financial Content / BizWire
consumer AI-ad survey (September 2026); Campaigns and Elections human-made ads study (2026); The
Current (2025); Digiday (2025); Forbes and TechRadar on Coca-Cola and McDonald's AI ads (2025);
Fortune on Skechers (2024); Stack Overflow Developer Survey (2025); Crowdin enterprise BYO-key survey
(2026); StorageNewsletter repatriation survey (2026); SaaS Mag data residency (2026); EU AI Act
Article 50 text and Commission FAQ (2026); Cooley, Faegre Drinker, Jones Day client alerts (2026);
Influencer Marketing Hub, Cinerads, Storrito on platform labelling (2026); Kapwing disclosure
statistics (2026).
