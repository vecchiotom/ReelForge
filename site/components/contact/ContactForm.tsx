'use client';

import { useRef, useState, type FocusEventHandler, type FormEvent } from 'react';
import { useRouter } from 'next/navigation';
import { validateContact, type ValidationError } from '@/lib/validation';
import { trackEvent } from '@/lib/gtag';
import { SUPPORT_EMAIL } from '@/lib/legal-placeholders';
import { Spinner } from '@/components/ui/Spinner';

type FieldName = 'name' | 'email' | 'company' | 'message';

interface FormValues {
  name: string;
  email: string;
  company: string;
  message: string;
}

const EMPTY_VALUES: FormValues = { name: '', email: '', company: '', message: '' };

type FormBannerError =
  | { kind: 'network' }
  | { kind: 'validation'; messages: string[] }
  | { kind: 'server' };

function errorsToMap(errors: ValidationError[]): Partial<Record<FieldName, string>> {
  const map: Partial<Record<FieldName, string>> = {};
  for (const err of errors) {
    if (err.field === 'name' || err.field === 'email' || err.field === 'company' || err.field === 'message') {
      // Keep the first message per field.
      if (!map[err.field]) {
        map[err.field] = err.message;
      }
    }
  }
  return map;
}

export function ContactForm() {
  const router = useRouter();
  const [values, setValues] = useState<FormValues>(EMPTY_VALUES);
  const [fieldErrors, setFieldErrors] = useState<Partial<Record<FieldName, string>>>({});
  const [submitting, setSubmitting] = useState(false);
  const [bannerError, setBannerError] = useState<FormBannerError | null>(null);

  const nameRef = useRef<HTMLInputElement>(null);
  const emailRef = useRef<HTMLInputElement>(null);
  const companyRef = useRef<HTMLInputElement>(null);
  const messageRef = useRef<HTMLTextAreaElement>(null);
  const bannerRef = useRef<HTMLDivElement>(null);
  const honeypotRef = useRef<HTMLInputElement>(null);

  const fieldRefs: Record<FieldName, React.RefObject<HTMLInputElement | HTMLTextAreaElement | null>> = {
    name: nameRef,
    email: emailRef,
    company: companyRef,
    message: messageRef,
  };

  function updateValue(field: FieldName, value: string) {
    setValues((prev) => ({ ...prev, [field]: value }));
  }

  function handleBlur(field: FieldName): FocusEventHandler<HTMLInputElement | HTMLTextAreaElement> {
    return () => {
      const errors = validateContact({ ...values, website: '' });
      const map = errorsToMap(errors);
      setFieldErrors((prev) => ({ ...prev, [field]: map[field] }));
    };
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBannerError(null);

    const website = honeypotRef.current?.value ?? '';
    const errors = validateContact({ ...values, website });
    const map = errorsToMap(errors);

    if (Object.keys(map).length > 0) {
      setFieldErrors(map);
      const firstInvalid = (['name', 'email', 'company', 'message'] as FieldName[]).find(
        (field) => map[field],
      );
      if (firstInvalid) {
        fieldRefs[firstInvalid].current?.focus();
      } else {
        bannerRef.current?.focus();
      }
      return;
    }

    setFieldErrors({});
    setSubmitting(true);

    const payload = { ...values, website };

    try {
      const response = await fetch('/api/contact', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(payload),
      });

      if (response.ok) {
        // A honeypot-filtered submission also returns `{ ok: true }` — it's
        // invisible to the real visitor and should look identical to a
        // genuine success from their point of view.
        trackEvent('generate_lead', { form: 'contact' });
        router.push('/contact/thank-you');
        return;
      }

      if (response.status === 400) {
        let serverMessages: string[] = [];
        try {
          const body = await response.json();
          if (Array.isArray(body?.errors)) {
            const serverErrors: ValidationError[] = body.errors;
            setFieldErrors((prev) => ({ ...prev, ...errorsToMap(serverErrors) }));
            serverMessages = serverErrors.map((e: ValidationError) => e.message);
          }
        } catch {
          // Fall through to the generic validation message below.
        }
        setBannerError({
          kind: 'validation',
          messages: serverMessages.length > 0 ? serverMessages : ['Please fix the highlighted fields.'],
        });
        bannerRef.current?.focus();
        return;
      }

      console.error('[contact] submission failed', response.status);
      setBannerError({ kind: 'server' });
      bannerRef.current?.focus();
    } catch (err) {
      console.error('[contact] submission failed', err);
      setBannerError({ kind: 'network' });
      bannerRef.current?.focus();
    } finally {
      setSubmitting(false);
    }
  }

  const inputClass = (field: FieldName) =>
    `mt-2 block w-full rounded-md border px-3 py-2 text-neutral-900 shadow-sm focus:outline-none focus:ring-1 ${
      fieldErrors[field]
        ? 'border-red-500 focus:border-red-500 focus:ring-red-500'
        : 'border-neutral-300 focus:border-brand-500 focus:ring-brand-500'
    }`;

  return (
    <form onSubmit={handleSubmit} noValidate className="space-y-6">
      {bannerError && (
        <div
          ref={bannerRef}
          role="alert"
          aria-live="assertive"
          tabIndex={-1}
          className="rounded-md border border-red-300 bg-red-50 p-4 text-sm text-red-800 focus:outline-none"
        >
          {bannerError.kind === 'network' && (
            <p>We couldn&apos;t reach the server — check your connection and try again.</p>
          )}
          {bannerError.kind === 'validation' && (
            <>
              <p>Please fix the highlighted fields.</p>
              {bannerError.messages.length > 0 && (
                <ul className="mt-2 list-inside list-disc">
                  {bannerError.messages.map((message) => (
                    <li key={message}>{message}</li>
                  ))}
                </ul>
              )}
            </>
          )}
          {bannerError.kind === 'server' && (
            <p>
              Something went wrong on our end. Email us at{' '}
              <a href={`mailto:${SUPPORT_EMAIL}`} className="font-medium underline">
                {SUPPORT_EMAIL}
              </a>{' '}
              and we&apos;ll pick it up.
            </p>
          )}
        </div>
      )}

      <div>
        <label htmlFor="name" className="block text-sm font-medium text-neutral-900">
          Name
        </label>
        <input
          ref={nameRef}
          id="name"
          name="name"
          type="text"
          required
          autoComplete="name"
          value={values.name}
          onChange={(e) => updateValue('name', e.target.value)}
          onBlur={handleBlur('name')}
          aria-invalid={fieldErrors.name ? 'true' : undefined}
          aria-describedby={fieldErrors.name ? 'name-error' : undefined}
          className={inputClass('name')}
        />
        {fieldErrors.name && (
          <p id="name-error" role="alert" className="mt-1 text-sm text-red-600">
            {fieldErrors.name}
          </p>
        )}
      </div>

      <div>
        <label htmlFor="email" className="block text-sm font-medium text-neutral-900">
          Email
        </label>
        <input
          ref={emailRef}
          id="email"
          name="email"
          type="email"
          required
          autoComplete="email"
          value={values.email}
          onChange={(e) => updateValue('email', e.target.value)}
          onBlur={handleBlur('email')}
          aria-invalid={fieldErrors.email ? 'true' : undefined}
          aria-describedby={fieldErrors.email ? 'email-error' : undefined}
          className={inputClass('email')}
        />
        {fieldErrors.email && (
          <p id="email-error" role="alert" className="mt-1 text-sm text-red-600">
            {fieldErrors.email}
          </p>
        )}
      </div>

      <div>
        <label htmlFor="company" className="block text-sm font-medium text-neutral-900">
          Company <span className="text-neutral-400">(optional)</span>
        </label>
        <input
          ref={companyRef}
          id="company"
          name="company"
          type="text"
          autoComplete="organization"
          value={values.company}
          onChange={(e) => updateValue('company', e.target.value)}
          onBlur={handleBlur('company')}
          aria-invalid={fieldErrors.company ? 'true' : undefined}
          aria-describedby={fieldErrors.company ? 'company-error' : undefined}
          className={inputClass('company')}
        />
        {fieldErrors.company && (
          <p id="company-error" role="alert" className="mt-1 text-sm text-red-600">
            {fieldErrors.company}
          </p>
        )}
      </div>

      <div>
        <label htmlFor="message" className="block text-sm font-medium text-neutral-900">
          Message
        </label>
        <textarea
          ref={messageRef}
          id="message"
          name="message"
          required
          rows={5}
          autoComplete="off"
          value={values.message}
          onChange={(e) => updateValue('message', e.target.value)}
          onBlur={handleBlur('message')}
          aria-invalid={fieldErrors.message ? 'true' : undefined}
          aria-describedby={fieldErrors.message ? 'message-error' : undefined}
          className={inputClass('message')}
        />
        {fieldErrors.message && (
          <p id="message-error" role="alert" className="mt-1 text-sm text-red-600">
            {fieldErrors.message}
          </p>
        )}
      </div>

      {/* Honeypot field — hidden from real users, left empty by bots that fill every field. */}
      <div className="absolute left-[-9999px] top-auto h-px w-px overflow-hidden">
        <label htmlFor="website">Website</label>
        <input
          ref={honeypotRef}
          id="website"
          name="website"
          type="text"
          tabIndex={-1}
          autoComplete="off"
          aria-hidden="true"
        />
      </div>

      <button
        type="submit"
        disabled={submitting}
        aria-busy={submitting}
        className="inline-flex min-h-11 min-w-[10rem] w-full items-center justify-center gap-2 rounded-md bg-brand-600 px-6 py-3 text-base font-semibold text-white hover:bg-brand-700 disabled:opacity-70 sm:w-auto"
      >
        {submitting ? (
          <>
            <Spinner className="h-4 w-4" />
            Sending…
          </>
        ) : (
          'Send message'
        )}
      </button>
    </form>
  );
}
