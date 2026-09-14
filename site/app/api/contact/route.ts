import { NextResponse } from 'next/server';

// NOTE: This route lives at /api/contact, NOT /api/v1/*, so nginx's
// `/api/v1/*` proxy rules (which forward to the Go API / inference service)
// never intercept it — it falls through to nginx's `location /` block and is
// served entirely by this Next.js marketing site. It has nothing to do with
// the Go/inference `/api/v1/*` APIs described in the repo's CLAUDE.md.
export async function POST(request: Request) {
  const payload = await request.json();

  // Honeypot: bots tend to fill every field. If it's non-empty, silently
  // swallow the submission without forwarding or logging it.
  if (typeof payload.website === 'string' && payload.website.trim().length > 0) {
    return NextResponse.json({ ok: true });
  }

  const webhookUrl = process.env.CONTACT_WEBHOOK_URL;
  if (webhookUrl) {
    await fetch(webhookUrl, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(payload),
    });
  } else {
    console.info('[contact]', payload);
  }

  return NextResponse.json({ ok: true });
}
