#!/bin/sh
# Renders nginx's :80/:443 server blocks and picks a TLS certificate before
# starting nginx. See docs/tls.md for the full picture; short version:
#
#   - No DOMAIN set (default): self-signed cert, generated once and reused,
#     so https://localhost works out of the box alongside http://.
#   - DOMAIN set and the `caddy` sidecar (compose `tls` profile) has already
#     obtained a Let's Encrypt cert for it: use that cert instead.
#
# Switching from "no domain" to "has domain" needs one manual container
# restart (this script re-runs and picks up the new cert path). After that,
# renewals are picked up automatically by the periodic `nginx -s reload`
# loop below — Caddy renews the file in place, nginx just needs to re-open it.
set -eu

DOMAIN="${DOMAIN:-}"
FORCE_HTTPS="${FORCE_HTTPS:-false}"
CERT_DIR=/etc/nginx/certs
CADDY_CERT_DIR="/caddy-data/caddy/certificates/acme-v02.api.letsencrypt.org-directory"

mkdir -p "$CERT_DIR" /etc/nginx/conf.d

if [ -n "$DOMAIN" ] && [ -f "$CADDY_CERT_DIR/$DOMAIN/$DOMAIN.crt" ]; then
    echo "[nginx] Using the Let's Encrypt certificate Caddy obtained for $DOMAIN"
    SSL_CERT="$CADDY_CERT_DIR/$DOMAIN/$DOMAIN.crt"
    SSL_KEY="$CADDY_CERT_DIR/$DOMAIN/$DOMAIN.key"
else
    if [ -n "$DOMAIN" ]; then
        echo "[nginx] DOMAIN=$DOMAIN is set but no certificate found yet at $CADDY_CERT_DIR/$DOMAIN/ — has the 'caddy' service (compose 'tls' profile) been started and finished issuing one? Falling back to a self-signed certificate for now."
    fi
    if [ ! -f "$CERT_DIR/selfsigned.crt" ]; then
        echo "[nginx] Generating a self-signed certificate for local development (this is expected to trigger a browser warning)"
        openssl req -x509 -nodes -days 365 -newkey rsa:2048 \
            -keyout "$CERT_DIR/selfsigned.key" -out "$CERT_DIR/selfsigned.crt" \
            -subj "/CN=${DOMAIN:-localhost}" \
            -addext "subjectAltName=DNS:${DOMAIN:-localhost}"
    fi
    SSL_CERT="$CERT_DIR/selfsigned.crt"
    SSL_KEY="$CERT_DIR/selfsigned.key"
fi

export SSL_CERT SSL_KEY
envsubst '${SSL_CERT} ${SSL_KEY}' < /etc/nginx/https-server.conf.template > /etc/nginx/conf.d/https-server.conf

if [ "$FORCE_HTTPS" = "true" ]; then
    echo "[nginx] FORCE_HTTPS=true — redirecting http:// to https://"
    cp /etc/nginx/http-server-redirect.conf /etc/nginx/conf.d/http-server.conf
else
    cp /etc/nginx/http-server-dev.conf /etc/nginx/conf.d/http-server.conf
fi

# Pick up a freshly (re)issued/renewed certificate without a full restart.
# Caddy renews roughly a month before expiry; checking twice a day is more
# than enough, and a reload is a cheap no-op when the file hasn't changed.
(
    while true; do
        sleep 43200
        nginx -s reload 2>/dev/null || true
    done
) &

exec nginx -g 'daemon off;'
