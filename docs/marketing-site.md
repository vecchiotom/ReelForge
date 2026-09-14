# Marketing Site

`/site` is a public marketing brochure — home, features, about, contact, legal — served at the
root domain. `/web` is the authenticated dashboard. They are two separate Next.js projects, not
one project with two route groups.

## Why a separate project instead of a route group in `/web`

- **Different audience, different trust boundary.** Every page in `/site` is meant to be crawled,
  indexed, and viewed by anonymous visitors with no session. Every page in `/web` requires a
  cookie and is deliberately kept out of search engines (see `robots.ts`/`sitemap.ts` in
  `/site` — neither exists in `/web`, whose middleware guards every route instead). Folding both
  into one Next app would mean threading "is this route public" through the same middleware that
  currently does one job: enforce auth.
- **Different tech needs.** `/web` is built on Mantine v8 for the component-heavy authenticated
  UI (tables, forms, drag-and-drop workflow builder). A marketing site doesn't need a component
  library at that scale — it needs fast static pages, so `/site` uses Tailwind CSS v4 directly and
  ships no Mantine at all. Pulling Mantine into pages that exist purely to be crawled and to load
  fast would add bundle weight, and pulling marketing-site concerns (JSON-LD, OG image generation,
  cookie consent) into `/web` would add weight in the other direction.
- **Independent deploy/build lifecycle.** `/site` has no backend dependency (no `depends_on` in
  `docker-compose.yml`) and must come up even if the entire backend stack is down — a marketing
  page returning an error because Postgres is unreachable is a worse failure mode than the whole
  platform being down. `/web` depends on `inference` and `go-api`. Two Next projects means each
  one's healthcheck, `depends_on`, and rebuild only touches what it actually needs.

## The routing split

Nginx is still the single entry point. The split happens in `nginx/locations.conf`, at the very
end of the file, using the two upstream variables declared per-scheme just above the routing table
(`nginx/http-server-dev.conf` for `:80`, `nginx/https-server.conf.template` for `:443`):

```nginx
set $web_app  http://web:3000;
set $site_app http://site:3000;
```

and the two trailing location blocks:

```nginx
# Dashboard (Next.js `web`, basePath '/app').
location ^~ /app {
    proxy_pass $web_app;
    ...
}

# Public marketing site.
location / {
    proxy_pass $site_app;
    ...
}
```

`location ^~ /app` wins over every regex `location` above it (SSE, `/stop`, etc.) without further
regex evaluation, and `web` itself runs with Next's `basePath: '/app'`, so `proxy_pass $web_app`
(a bare variable, no URI segment) forwards `/app/...` through unchanged — exactly what a
basePath'd app expects to receive. Everything that isn't `/api/v1/*`, `/health`, or `/app/*` falls
through to the trailing `location /`, which proxies to `site`. Every location block *before* these
two (auth, admin, workflows, the SSE regex, the `/stop` regex, health, workflow-engine, the
sandbox-executor's deliberate non-block, the generic `/api/v1/` catch-all) is unrelated to this
split and untouched by it.

## Why `web`'s healthcheck probes `/app/login`, not `/`

`web` runs with `basePath: '/app'`, so a request for `/` inside that container 404s — Next simply
doesn't have a route there. `docker-compose.yml`'s original healthcheck (`wget .../` ) predates the
basePath change and would now report `web` unhealthy forever. That matters beyond a red status
dot: `nginx`'s `depends_on` on `web` uses `condition: service_healthy`, so an unhealthy `web`
would mean nginx itself never starts, taking the entire stack down over a healthcheck that was
probing a route that doesn't exist. The healthcheck was moved to a real, unauthenticated route
under the basePath — `/app/login` — which 200s regardless of auth state.

## The single-swap domain contract

Every place in `/site` that needs the real, absolute domain — canonical URLs, `sitemap.xml`,
`robots.txt`'s `sitemap`/`host` fields, Open Graph and Twitter card URLs, JSON-LD `url` fields —
reads it from exactly one place: `siteConfig.url` in `site/lib/site-config.ts`, which in turn reads
`process.env.NEXT_PUBLIC_SITE_URL` (falling back to `https://reelforge.com` if unset). `buildMetadata()`
in `site/lib/seo.ts` is the only place page-level metadata is constructed, and it always resolves
URLs through `siteConfig.url`. There is deliberately no second place that hardcodes a domain — swap
`NEXT_PUBLIC_SITE_URL` once and every page, feed, and structured-data block follows.

## Launch checklist

Work through this before pointing real traffic (and real search engines) at the site.

1. **Set the real domain.** `NEXT_PUBLIC_SITE_URL` in `.env` — see "single-swap domain contract"
   above. This is a build-time arg (`site/Dockerfile` passes it to `next build`), so the `site`
   image must be rebuilt after changing it, not just restarted.
2. **Fill in every bracketed placeholder in `site/lib/legal-placeholders.ts`.** Each constant is an
   intentionally unfilled `[TOKEN]` — nobody but the business owner has these facts, so none of
   them were invented. The full list:
   - `COMPANY_LEGAL_NAME` — the registered legal entity name
   - `REGISTERED_ADDRESS` — registered/business address shown in the footer and legal pages
   - `COMPANY_REGISTRATION_NUMBER` — company/business registration number
   - `VAT_NUMBER` — VAT or equivalent tax ID
   - `SUPPORT_EMAIL` — general contact/support address
   - `PRIVACY_EMAIL` — privacy-specific contact address (may equal `SUPPORT_EMAIL`)
   - `DPO_CONTACT` — data protection officer contact, if one is required/appointed
   - `GOVERNING_LAW` — the jurisdiction whose law governs the terms
   - `COURTS_JURISDICTION` — which courts have jurisdiction over disputes
   - `LAST_UPDATED` — the date the legal pages were last revised
   - `PHONE_NUMBER` — business phone number
   - `HOSTING_PROVIDER` — named in the privacy policy's sub-processor disclosure
   - `EMAIL_PROVIDER` — named alongside the hosting provider
   - `SUPERVISORY_AUTHORITY` — the data-protection authority visitors can complain to
   - `CONTACT_RETENTION_PERIOD` — how long contact-form submissions are retained
   - `LOG_RETENTION_PERIOD` — how long server/access logs are retained
   - `LIABILITY_CAP` — the liability limitation figure/formula in the terms
   - `SERVICE_AGREEMENT_REFERENCE` — reference to any separate master service agreement
   - `TRANSFER_MECHANISM` — the mechanism (e.g. SCCs) governing any international data transfer
   - `RESPONSE_TIME_SLA` — the response-time commitment shown on the contact page
3. **Analytics.** Set `NEXT_PUBLIC_GA_MEASUREMENT_ID` to a real GA4 measurement ID
   (`G-XXXXXXXXXX`), or leave it empty to ship with no analytics at all — no script is injected
   either way unless a visitor also accepts the cookie banner (see below).
4. **Contact form delivery.** Set `CONTACT_WEBHOOK_URL` if submissions should be POSTed onward
   (e.g. to a CRM or Slack webhook) instead of only being logged to the `site` container's stdout.
5. **Switch the build to production.** Set `SITE_BUILD_TARGET=production` and
   `SITE_NODE_ENV=production` in `.env`, and comment out or remove the dev-only volume mounts
   under `docker-compose.yml`'s `site` service (`./site:/app`, `/app/node_modules`, `/app/.next`)
   — those mounts exist for Turbopack hot reload and would shadow the precompiled standalone build
   the production Dockerfile target produces.
6. **Submit to search engines.** Once the real domain is live and serving real content, submit
   `https://<domain>/sitemap.xml` to Google Search Console (and any other search console you use).

## Cookie consent gates GA4 — and undercounts pageviews on purpose

`CookieConsentProvider` (`site/components/consent/CookieConsentProvider.tsx`) tracks one of three
states — no choice yet, granted, denied — in `localStorage`. `GoogleAnalytics`
(`site/components/analytics/GoogleAnalytics.tsx`) will not inject the `gtag.js` script tag, call
`gtag()`, or set any GA cookie unless that state is exactly `'granted'`. This is deliberate: no
script loads before a choice is made, and none loads at all if the visitor rejects or simply never
interacts with the banner (`CookieBanner`, fixed to the viewport bottom until a choice is made).

The direct consequence: **GA4 will undercount total visits** by whatever fraction of visitors
reject the banner or leave before answering it. This is not a bug to "fix" by loading analytics
earlier or by treating silence as consent — it is the privacy tradeoff the consent-gating design
exists to make, and any pageview number pulled from GA4 should be read as a floor, not a total.

## No invented facts

Two categories of content went into this site, and they were held to different rules:

- **Product capabalities** (what ReelForge does, its architecture, its workflow pipeline) were
  written from `CLAUDE.md` and the actual codebase — real, verifiable facts about a real system.
- **Business/legal facts** that only the business owner can supply — legal entity name, registered
  address, registration/VAT numbers, DPO contact, governing law, liability caps, retention
  periods, response-time SLAs — were never invented, guessed, or filled with a plausible-looking
  placeholder. Every one of them is a literal `[BRACKETED_TOKEN]` in
  `site/lib/legal-placeholders.ts`, imported everywhere it's needed (footer, contact page, privacy
  policy, terms of service) so there is exactly one place to fill in real values, and no
  page silently shipped with a fabricated address or a made-up company name that could be mistaken
  for real. See the launch checklist above for the full list of tokens to replace before launch.
