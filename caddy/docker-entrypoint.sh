#!/bin/sh
# Guards against starting Caddy with an unusable Caddyfile: {$DOMAIN} as an
# empty site address is invalid, and Caddy's own resulting error is not
# obvious about what to actually go fix. See docs/tls.md.
set -eu

if [ -z "${DOMAIN:-}" ] || [ -z "${ACME_EMAIL:-}" ]; then
    echo "DOMAIN and ACME_EMAIL must both be set in .env before starting the 'caddy' service. See docs/tls.md." >&2
    exit 1
fi

exec caddy run --config /etc/caddy/Caddyfile --adapter caddyfile
