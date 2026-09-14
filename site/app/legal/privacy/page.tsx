import { buildMetadata } from '@/lib/seo';
import { siteConfig } from '@/lib/site-config';
import { CookiePreferencesLink } from '@/components/consent/CookiePreferencesLink';
import {
  COMPANY_LEGAL_NAME,
  COMPANY_REGISTRATION_NUMBER,
  CONTACT_RETENTION_PERIOD,
  DPO_CONTACT,
  EMAIL_PROVIDER,
  HOSTING_PROVIDER,
  LOG_RETENTION_PERIOD,
  PRIVACY_EMAIL,
  REGISTERED_ADDRESS,
  SUPERVISORY_AUTHORITY,
  TRANSFER_MECHANISM,
} from '@/lib/legal-placeholders';

export const metadata = buildMetadata({
  title: 'Privacy Policy',
  description: `How ${siteConfig.name} collects, uses, and protects information from visitors to this marketing website.`,
  path: '/legal/privacy',
});

const h2 = 'mt-10 text-xl font-bold tracking-tight text-neutral-900 first:mt-0';
const p = 'mt-4 text-neutral-600';
const ul = 'mt-4 list-disc space-y-2 pl-5 text-neutral-600';

export default function PrivacyPolicyPage() {
  return (
    <>
      <h1 className="text-3xl font-bold tracking-tight text-neutral-900 sm:text-4xl">
        Privacy Policy
      </h1>
      <p className={p}>
        This Privacy Policy explains how {COMPANY_LEGAL_NAME} (&quot;we&quot;, &quot;us&quot;, or
        &quot;our&quot;) collects, uses, discloses, and protects information in connection with the{' '}
        {siteConfig.name} public marketing website. Please read it carefully. If you have
        questions, contact us using the details in Section 12.
      </p>

      <h2 className={h2}>1. Who we are</h2>
      <p className={p}>
        The {siteConfig.name} marketing website is operated by:
      </p>
      <ul className={ul}>
        <li>
          <strong>Company:</strong> {COMPANY_LEGAL_NAME}
        </li>
        <li>
          <strong>Registered address:</strong> {REGISTERED_ADDRESS}
        </li>
        <li>
          <strong>Company registration number:</strong> {COMPANY_REGISTRATION_NUMBER}
        </li>
        <li>
          <strong>Privacy contact:</strong> {PRIVACY_EMAIL}
        </li>
        <li>
          <strong>Data protection officer / contact:</strong> {DPO_CONTACT}
        </li>
      </ul>

      <h2 className={h2}>2. Scope</h2>
      <p className={p}>
        This policy covers only the public marketing website you are currently reading — the pages
        served at the root domain, such as the homepage, features, about, and contact pages. If you
        sign up for and use the {siteConfig.name} application itself (reached through the
        &quot;Sign in&quot; link, under the <code>/app</code> path), that use is governed by
        separate, account-level data handling terms presented within the application at sign-up,
        not by this document. This distinction matters because the marketing site and the
        application are different services with different data practices: the marketing site is
        essentially informational and handles contact-form submissions and site analytics, while
        the application processes project files, workflow data, and account information under a
        different legal basis (contract performance for a signed-up customer).
      </p>

      <h2 className={h2}>3. What we collect</h2>
      <p className={p}>On this marketing website, we collect the following categories of data:</p>
      <ul className={ul}>
        <li>
          <strong>Contact-form submissions.</strong> When you use the contact form, we collect the
          name, email address, company name (optional), and message you provide.
        </li>
        <li>
          <strong>Server access logs.</strong> Like most websites, our hosting infrastructure
          automatically records technical information about each request, including your IP
          address, user-agent string, requested URL, referring page, and timestamp.
        </li>
        <li>
          <strong>Analytics data.</strong> If — and only if — you affirmatively accept cookies via
          the cookie banner, we load Google Analytics, which collects usage data such as pages
          viewed, session duration, device/browser type, and approximate location derived from your
          IP address. No analytics data is collected before you make that choice.
        </li>
      </ul>

      <h2 className={h2}>4. Why we collect it, and our lawful basis</h2>
      <p className={p}>
        We only process personal data where we have a valid lawful basis to do so. The table below
        summarizes the purpose and legal basis for each category described in Section 3.
      </p>
      <ul className={ul}>
        <li>
          <strong>Contact-form data</strong> is processed to respond to your enquiry and, where
          applicable, to take steps you request prior to entering into a contract with us. Lawful
          basis: performance of, or steps prior to, a contract, and/or our legitimate interest in
          responding to enquiries directed at us.
        </li>
        <li>
          <strong>Server access logs</strong> are processed to operate, secure, and troubleshoot the
          website (for example, detecting abuse or diagnosing errors). Lawful basis: legitimate
          interest in keeping the website secure and functioning correctly.
        </li>
        <li>
          <strong>Analytics data</strong> is processed to understand how visitors use the site and
          to improve it. Lawful basis: your consent, obtained through the cookie banner described in
          Section 5. You may withdraw this consent at any time.
        </li>
      </ul>

      <h2 className={h2}>5. Cookies and similar technologies</h2>
      <p className={p}>
        We use a small number of storage mechanisms, summarized below. We do not set any cookie
        before you make a choice in the cookie banner — your initial choice itself is remembered in
        your browser&apos;s local storage, not a cookie.
      </p>
      <div className="mt-4 overflow-x-auto">
        <table className="w-full min-w-[560px] border-collapse text-left text-sm text-neutral-600">
          <thead>
            <tr className="border-b border-neutral-300 text-neutral-900">
              <th className="py-2 pr-4 font-semibold">Name</th>
              <th className="py-2 pr-4 font-semibold">Type</th>
              <th className="py-2 pr-4 font-semibold">Purpose</th>
              <th className="py-2 pr-4 font-semibold">Duration</th>
            </tr>
          </thead>
          <tbody>
            <tr className="border-b border-neutral-200 align-top">
              <td className="py-2 pr-4 font-mono text-xs">rf-cookie-consent</td>
              <td className="py-2 pr-4">Local storage (not a cookie)</td>
              <td className="py-2 pr-4">
                Strictly necessary — remembers whether you accepted or rejected analytics cookies,
                so we don&apos;t ask again on every visit.
              </td>
              <td className="py-2 pr-4">Persistent, until you clear it or change your choice</td>
            </tr>
            <tr className="border-b border-neutral-200 align-top">
              <td className="py-2 pr-4 font-mono text-xs">_ga</td>
              <td className="py-2 pr-4">Cookie (Google Analytics)</td>
              <td className="py-2 pr-4">Analytics — distinguishes unique visitors. Set only after you accept.</td>
              <td className="py-2 pr-4">Typically 2 years</td>
            </tr>
            <tr className="align-top">
              <td className="py-2 pr-4 font-mono text-xs">_ga_&lt;container-id&gt;</td>
              <td className="py-2 pr-4">Cookie (Google Analytics)</td>
              <td className="py-2 pr-4">
                Analytics — persists session state for a specific Google Analytics property. Set
                only after you accept.
              </td>
              <td className="py-2 pr-4">Typically 2 years (session cookie variants: 24 hours)</td>
            </tr>
          </tbody>
        </table>
      </div>
      <p className={p}>
        You can change your mind at any time: <CookiePreferencesLink className="text-brand-600 hover:text-brand-700" /> reopens the
        consent banner so you can accept or reject analytics cookies again.
      </p>

      <h2 className={h2}>6. Sharing and processors</h2>
      <p className={p}>
        We do not sell your personal data. We share limited data with the service providers below,
        each acting on our behalf under appropriate contractual terms:
      </p>
      <ul className={ul}>
        <li>
          <strong>Hosting:</strong> {HOSTING_PROVIDER} hosts the website and processes server access
          logs as part of delivering the site to you.
        </li>
        <li>
          <strong>Analytics:</strong> Google Analytics, provided by Google Ireland Ltd, processes
          analytics data described in Section 3, but only once you have given consent.
        </li>
        <li>
          <strong>Email:</strong> {EMAIL_PROVIDER} processes contact-form submissions in order to
          deliver them to our team and allow us to reply to you.
        </li>
      </ul>

      <h2 className={h2}>7. International transfers</h2>
      <p className={p}>
        Some of the processors listed in Section 6 may process data outside of your own country or
        region. Where that happens, we rely on the following transfer mechanism to ensure your data
        continues to receive an adequate level of protection: {TRANSFER_MECHANISM}.
      </p>

      <h2 className={h2}>8. Retention</h2>
      <p className={p}>
        We keep contact-form submissions for {CONTACT_RETENTION_PERIOD}, after which they are
        deleted or anonymized unless a longer period is required to resolve an ongoing enquiry or
        comply with a legal obligation. Server access logs are retained for {LOG_RETENTION_PERIOD}
        for security and troubleshooting purposes, after which they are automatically purged.
        Analytics data is retained according to Google Analytics&apos; own configured retention
        settings for our property.
      </p>

      <h2 className={h2}>9. Your rights</h2>
      <p className={p}>
        Depending on where you live, you may have some or all of the following rights over your
        personal data:
      </p>
      <ul className={ul}>
        <li>The right to access the personal data we hold about you</li>
        <li>The right to have inaccurate personal data corrected (rectification)</li>
        <li>The right to have your personal data deleted in certain circumstances (erasure)</li>
        <li>The right to restrict how we process your personal data in certain circumstances</li>
        <li>The right to receive your data in a portable format (data portability)</li>
        <li>The right to object to processing based on legitimate interest</li>
        <li>The right to withdraw consent at any time, for processing based on consent (such as analytics)</li>
        <li>
          The right to lodge a complaint with a supervisory authority, in particular{' '}
          {SUPERVISORY_AUTHORITY}
        </li>
      </ul>
      <p className={p}>
        To exercise any of these rights, contact us using the details in Section 12. We will
        respond in accordance with applicable law.
      </p>

      <h2 className={h2}>10. Children</h2>
      <p className={p}>
        This website is directed at businesses and professionals evaluating {siteConfig.name}. It
        is not directed at, and we do not knowingly collect personal data from, children. If you
        believe a child has provided us with personal data, please contact us so we can delete it.
      </p>

      <h2 className={h2}>11. Changes to this policy</h2>
      <p className={p}>
        We may update this policy from time to time to reflect changes in our practices or for
        legal, operational, or regulatory reasons. We will update the &quot;Last updated&quot; date
        at the top of this page when we do. Material changes will be highlighted in a more prominent
        way where appropriate.
      </p>

      <h2 className={h2}>12. How to contact us</h2>
      <p className={p}>If you have questions about this policy or how we handle your data, contact us at:</p>
      <ul className={ul}>
        <li>{COMPANY_LEGAL_NAME}</li>
        <li>{REGISTERED_ADDRESS}</li>
        <li>
          Privacy enquiries: <a href={`mailto:${PRIVACY_EMAIL}`} className="text-brand-600 hover:text-brand-700">{PRIVACY_EMAIL}</a>
        </li>
        <li>Data protection officer / contact: {DPO_CONTACT}</li>
      </ul>
    </>
  );
}
