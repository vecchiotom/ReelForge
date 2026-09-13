# TLS

nginx terminates TLS on `:443`. A separate `caddy` container is used purely as
an ACME client to obtain and auto-renew a free Let's Encrypt certificate once
you have a real domain — it never serves application traffic itself.

## Why two containers instead of one

Caddy's headline feature is automatic HTTPS, but this stack's routing (the
cookie↔`Authorization`-header translation in `nginx/auth.js`, the sandbox's
deliberately-unproxied path, per-path `proxy_read_timeout`s for SSE, etc.) is
already implemented in nginx/njs. Replacing nginx would mean re-implementing
all of that. Instead:

- **nginx** stays the single public entry point on `:80`/`:443` and keeps
  100% of its existing routing, unchanged.
- **Caddy** only ever talks to Let's Encrypt. It isn't published to the host
  and is unreachable from outside the `reelforge` docker network.

nginx reads the certificate Caddy obtains directly off a shared volume
(`caddy_certs`, mounted read-only into nginx). There is no copying, syncing,
or cross-container signaling: renewals just update the file in place, and
nginx's own background loop (in `nginx/docker-entrypoint.sh`) runs
`nginx -s reload` every 12 hours to pick up the new file — a no-op when
nothing changed.

## Local development (no domain)

Nothing to configure. `docker compose up` generates a self-signed certificate
the first time nginx starts (`nginx/docker-entrypoint.sh`) and serves it on
`:443` alongside the existing `:80`. Browsers will show a certificate warning
for the self-signed cert — that's expected, not a bug. `FORCE_HTTPS` defaults
to `false`, so `http://localhost` keeps working exactly as before TLS support
was added.

## Going live with a real domain

1. Point the domain's DNS `A`/`AAAA` record at this host's public IP, and
   make sure `:80` and `:443` are actually reachable from the internet (this
   is required for Let's Encrypt's HTTP-01 challenge, which always connects
   on port 80 first).
2. Set `DOMAIN` and `ACME_EMAIL` in `.env`.
3. `docker compose --profile tls up -d caddy` — Caddy is opt-in via the `tls`
   compose profile so it never even starts (let alone crash-loops on a
   missing `DOMAIN`) for anyone who hasn't gone through this setup.
4. `docker compose up -d --force-recreate nginx` — nginx only re-evaluates
   which certificate to load at container start (`nginx/docker-entrypoint.sh`
   runs once, at boot), so switching from "no domain" to "has a domain" needs
   this one manual recreate. After this, renewals are automatic — no further
   steps, ever.
5. Confirm `https://<DOMAIN>` works and shows a real, trusted certificate.
6. Only then, set `FORCE_HTTPS=true` in `.env` and restart nginx. This makes
   `http://` redirect to `https://`. Flipping it on before step 5 is
   confirmed just replaces a working `http://` dev flow with a broken or
   self-signed-warning `https://` one.
7. Set `COOKIE_SECURE=true` in `.env` and restart `go-api`. Do this only
   after `https://<DOMAIN>` is confirmed working — browsers silently drop
   `Secure` cookies sent over plain HTTP, so setting this too early breaks
   login with no obvious error.

## How the pieces fit together

- `nginx/Dockerfile` — extends `nginx:alpine` with `openssl` (self-signed
  cert generation) and `gettext` (`envsubst`, used to render the HTTPS
  server block's certificate paths).
- `nginx/docker-entrypoint.sh` — on every container start: picks a
  certificate (Caddy's, if `DOMAIN` is set and one has actually been issued;
  otherwise a self-signed one, generated once and reused), renders
  `nginx/https-server.conf.template` into `/etc/nginx/conf.d/https-server.conf`,
  copies either `nginx/http-server-dev.conf` or
  `nginx/http-server-redirect.conf` into `/etc/nginx/conf.d/http-server.conf`
  depending on `FORCE_HTTPS`, starts the periodic reload loop, then execs
  nginx.
- `nginx/locations.conf` — the actual routing table (unchanged from before
  TLS support), shared via `include` by both the `:80` (dev mode) and `:443`
  server blocks so it only exists in one place.
- `nginx/http-server-dev.conf` / `nginx/http-server-redirect.conf` — the two
  possible `:80` server blocks (passthrough vs. redirect-to-https). Both also
  forward `/.well-known/acme-challenge/` to the `caddy` container, which is
  how Let's Encrypt's HTTP-01 validation reaches Caddy even though Caddy
  itself is never exposed to the internet.
- `caddy/Caddyfile` — a minimal site block for `{$DOMAIN}` that does nothing
  but respond 200 (Caddy manages a certificate for any domain used as a site
  address in its config, automatically, on startup).
- `caddy/docker-entrypoint.sh` — refuses to start with a clear error if
  `DOMAIN`/`ACME_EMAIL` aren't set, instead of Caddy's own less obvious error
  from an empty `{$DOMAIN}` site address.

Caddy's on-disk certificate storage layout is a stable, documented
convention: `/data/caddy/certificates/acme-v02.api.letsencrypt.org-directory/<domain>/<domain>.crt`
(and `.key`) for the production Let's Encrypt CA. `nginx/docker-entrypoint.sh`
reads this path directly (mounted read-only at `/caddy-data`) — there's
nothing else keeping the two containers in sync.
