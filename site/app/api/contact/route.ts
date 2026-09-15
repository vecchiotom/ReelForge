import { NextResponse } from 'next/server';
import { validateContact } from '@/lib/validation';

// NOTE: This route lives at /api/contact, NOT /api/v1/*, so nginx's
// `/api/v1/*` proxy rules (which forward to the Go API / inference service)
// never intercept it — it falls through to nginx's `location /` block and is
// served entirely by this Next.js marketing site. It has nothing to do with
// the Go/inference `/api/v1/*` APIs described in the repo's CLAUDE.md.

const RATE_LIMIT_WINDOW_MS = 10 * 60 * 1000;
const RATE_LIMIT_MAX_REQUESTS = 5;
const FALLBACK_RATE_LIMIT_KEY = 'unknown';

// Best-effort, in-memory, per-IP rate limit. Resets on redeploy/restart and
// is per-instance (not shared across replicas) — acceptable for a marketing
// contact form, not a substitute for a real edge/WAF rate limiter.
const requestLog = new Map<string, number[]>();

function rateLimitKey(request: Request): string {
  const forwardedFor = request.headers.get('x-forwarded-for');
  if (forwardedFor) {
    return forwardedFor.split(',')[0].trim() || FALLBACK_RATE_LIMIT_KEY;
  }
  return request.headers.get('x-real-ip') ?? FALLBACK_RATE_LIMIT_KEY;
}

function isRateLimited(key: string): boolean {
  const now = Date.now();
  const windowStart = now - RATE_LIMIT_WINDOW_MS;
  const timestamps = (requestLog.get(key) ?? []).filter((ts) => ts > windowStart);

  if (timestamps.length >= RATE_LIMIT_MAX_REQUESTS) {
    requestLog.set(key, timestamps);
    return true;
  }

  timestamps.push(now);
  requestLog.set(key, timestamps);
  return false;
}

export async function POST(request: Request) {
  const key = rateLimitKey(request);
  if (isRateLimited(key)) {
    return NextResponse.json({ ok: false, errors: [{ field: '_form', message: 'Too many requests. Please try again later.' }] }, { status: 429 });
  }

  const payload = await request.json();

  // Honeypot: bots tend to fill every field. If it's non-empty, silently
  // swallow the submission without forwarding, logging, or validating it —
  // a real visitor never sees or triggers this branch, so from their
  // perspective a filled honeypot and a genuine success look identical.
  if (typeof payload.website === 'string' && payload.website.trim().length > 0) {
    return NextResponse.json({ ok: true });
  }

  // Server-side validation is authoritative — never trust that the client
  // already validated correctly (a direct API call bypassing the UI must
  // still be rejected for bad input).
  const errors = validateContact(payload);
  if (errors.length > 0) {
    return NextResponse.json({ ok: false, errors }, { status: 400 });
  }

  const webhookUrl = process.env.CONTACT_WEBHOOK_URL;
  if (webhookUrl) {
    try {
      const webhookResponse = await fetch(webhookUrl, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(payload),
      });

      if (!webhookResponse.ok) {
        console.error('[contact] webhook responded with', webhookResponse.status);
        return NextResponse.json(
          { ok: false, errors: [{ field: '_form', message: 'Failed to deliver message.' }] },
          { status: 502 },
        );
      }
    } catch (err) {
      console.error('[contact] webhook request failed', err);
      return NextResponse.json(
        { ok: false, errors: [{ field: '_form', message: 'Failed to deliver message.' }] },
        { status: 502 },
      );
    }
  } else {
    console.info('[contact]', payload);
  }

  return NextResponse.json({ ok: true });
}
