// Plain, framework-free validation shared by the contact form's client-side
// component (for inline/on-blur feedback) and the `/api/contact` route (the
// authoritative, server-side check). No React/Next imports — must run
// unmodified in both a browser bundle and a Node/Edge route handler.

export interface ValidationError {
  field: string;
  message: string;
}

export interface ContactInput {
  name: string;
  email: string;
  company?: string;
  message?: string;
  website?: string;
}

// Reasonably strict without trying to be a full RFC 5322 implementation.
const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export function validateContact(input: ContactInput): ValidationError[] {
  const errors: ValidationError[] = [];

  const name = (input.name ?? '').trim();
  if (!name) {
    errors.push({ field: 'name', message: 'Please enter your name.' });
  } else if (name.length < 2 || name.length > 100) {
    errors.push({ field: 'name', message: 'Name must be between 2 and 100 characters.' });
  }

  const email = (input.email ?? '').trim();
  if (!email) {
    errors.push({ field: 'email', message: 'Please enter your email address.' });
  } else if (email.length > 254 || !EMAIL_RE.test(email)) {
    errors.push({ field: 'email', message: 'Please enter a valid email address.' });
  }

  const company = (input.company ?? '').trim();
  if (company.length > 100) {
    errors.push({ field: 'company', message: 'Company name must be 100 characters or fewer.' });
  }

  const message = (input.message ?? '').trim();
  if (!message) {
    errors.push({ field: 'message', message: 'Please enter a message.' });
  } else if (message.length < 20 || message.length > 5000) {
    errors.push({ field: 'message', message: 'Message must be between 20 and 5000 characters.' });
  }

  // Honeypot: a real visitor never fills this in. The API route already
  // short-circuits (silently, before validation runs) when it's filled, but
  // flag it here too so this function is a complete, honest description of
  // "what a valid submission looks like" for any other caller.
  const website = (input.website ?? '').trim();
  if (website.length > 0) {
    errors.push({ field: 'website', message: 'Invalid submission.' });
  }

  return errors;
}
