import { buildMetadata } from '@/lib/seo';
import { siteConfig } from '@/lib/site-config';
import {
  COMPANY_LEGAL_NAME,
  COURTS_JURISDICTION,
  GOVERNING_LAW,
  LIABILITY_CAP,
  PRIVACY_EMAIL,
  REGISTERED_ADDRESS,
  SERVICE_AGREEMENT_REFERENCE,
  SUPPORT_EMAIL,
} from '@/lib/legal-placeholders';

export const metadata = buildMetadata({
  title: 'Terms & Conditions',
  description: `The terms governing your use of the ${siteConfig.name} marketing website.`,
  path: '/legal/terms',
});

const h2 = 'mt-10 font-display text-xl font-bold uppercase tracking-[-0.01em] text-ink first:mt-0';
const p = 'mt-4 font-sans text-ink-muted';
const ul = 'mt-4 list-disc space-y-2 pl-5 font-sans text-ink-muted';

export default function TermsPage() {
  return (
    <>
      <h1 className="font-display text-3xl font-bold uppercase tracking-[-0.02em] text-ink sm:text-4xl">
        Terms &amp; Conditions
      </h1>
      <p className={p}>
        These Terms &amp; Conditions (&quot;Terms&quot;) govern your access to and use of the{' '}
        {siteConfig.name} public marketing website. By browsing or using this website, you agree to
        these Terms. If you do not agree, please do not use this website.
      </p>

      <h2 className={h2}>1. Who these terms are with</h2>
      <p className={p}>
        These Terms are between you and {COMPANY_LEGAL_NAME}, registered at {REGISTERED_ADDRESS}{' '}
        (&quot;we&quot;, &quot;us&quot;, &quot;our&quot;).
      </p>

      <h2 className={h2}>2. Acceptance</h2>
      <p className={p}>
        By accessing any page of this website, submitting the contact form, or otherwise using any
        part of the site, you confirm that you accept these Terms and agree to comply with them. We
        may update these Terms from time to time as described in Section 11; continued use of the
        website after an update constitutes acceptance of the revised Terms.
      </p>

      <h2 className={h2}>3. Scope</h2>
      <p className={p}>
        These Terms cover only your use of this public marketing website. They do not cover your
        use of the {siteConfig.name} application itself, which you may reach via the &quot;Sign
        in&quot; link on this site. Use of the application is instead governed by{' '}
        {SERVICE_AGREEMENT_REFERENCE}, agreed separately at the point of sign-up.
      </p>

      <h2 className={h2}>4. Permitted use and restrictions</h2>
      <p className={p}>
        You may use this website for lawful purposes only, and in a way that does not infringe the
        rights of, restrict, or inhibit anyone else&apos;s use of it. In particular, you agree not
        to:
      </p>
      <ul className={ul}>
        <li>
          Scrape, crawl, or systematically extract content from the website using automated means,
          except for standard, well-behaved search-engine indexing
        </li>
        <li>Reverse engineer, decompile, or disassemble any part of the website or its underlying software</li>
        <li>
          Submit the contact form, or otherwise interact with the website, in a way that constitutes
          spam, abuse, or automated misuse
        </li>
        <li>Attempt to gain unauthorized access to any part of the website, its systems, or related networks</li>
        <li>Introduce malware, or otherwise interfere with the normal operation of the website</li>
      </ul>

      <h2 className={h2}>5. Intellectual property</h2>
      <p className={p}>
        Unless otherwise indicated, all content on this website — including text, graphics, logos,
        and the {siteConfig.name} name and marks — is owned by or licensed to us and is protected by
        applicable intellectual property laws. You may view and print pages from the website for
        your own personal or internal business use, but you may not otherwise reproduce, modify,
        distribute, or republish any content without our prior written consent.
      </p>
      <p className={p}>
        {siteConfig.name} the product is built using, and is grateful to, a number of third-party
        open-source tools, including Remotion (for programmatic video rendering) and ffmpeg (for
        video processing). Those tools remain the property of their respective owners and are used
        under their respective licenses; nothing in these Terms grants you any rights in that
        third-party software.
      </p>

      <h2 className={h2}>6. User submissions via the contact form</h2>
      <p className={p}>
        When you submit information through the contact form, you grant us a non-exclusive,
        worldwide, royalty-free license to use that information for the purpose of responding to
        your enquiry and for our internal business purposes related to that enquiry, consistent with
        our Privacy Policy. You represent and warrant that you have the right to submit the
        information you provide, and that it does not infringe or violate the rights of any third
        party.
      </p>

      <h2 className={h2}>7. Third-party links and services</h2>
      <p className={p}>
        This website may link to third-party websites or services that are not owned or controlled
        by us. We are not responsible for the content, privacy practices, or terms of any
        third-party website or service, and linking to them does not imply our endorsement. You
        access any third-party website or service entirely at your own risk.
      </p>

      <h2 className={h2}>8. Availability and &quot;as is&quot; provision</h2>
      <p className={p}>
        We aim to keep this website available and up to date, but we do not guarantee that it will
        always be available, uninterrupted, secure, or error-free. The website and its content are
        provided &quot;as is&quot; and &quot;as available&quot;, without warranties of any kind,
        whether express or implied, to the fullest extent permitted by applicable law.
      </p>

      <h2 className={h2}>9. Limitation of liability</h2>
      <p className={p}>
        To the fullest extent permitted by applicable law, our total liability to you arising out of
        or in connection with your use of this website, whether in contract, tort (including
        negligence), or otherwise, shall not exceed {LIABILITY_CAP}. Nothing in these Terms excludes
        or limits any liability that cannot lawfully be excluded or limited, including liability for
        death or personal injury caused by negligence, fraud, or fraudulent misrepresentation, or any
        statutory rights you may have as a consumer that cannot be waived by agreement.
      </p>

      <h2 className={h2}>10. Indemnity</h2>
      <p className={p}>
        You agree to indemnify and hold us harmless from any claims, losses, liabilities, damages,
        costs, and expenses (including reasonable legal fees) arising out of your breach of these
        Terms or your misuse of the website.
      </p>

      <h2 className={h2}>11. Changes to these terms</h2>
      <p className={p}>
        We may revise these Terms at any time by updating this page. The &quot;Last updated&quot;
        date at the top of this document reflects the most recent revision. Please check back
        periodically; your continued use of the website after changes are posted constitutes
        acceptance of the revised Terms.
      </p>

      <h2 className={h2}>12. Governing law and jurisdiction</h2>
      <p className={p}>
        These Terms are governed by and construed in accordance with the laws of {GOVERNING_LAW},
        without regard to its conflict-of-laws principles. You agree to submit to the exclusive
        jurisdiction of the courts of {COURTS_JURISDICTION} to resolve any dispute arising out of or
        relating to these Terms or your use of the website.
      </p>

      <h2 className={h2}>13. Contact</h2>
      <p className={p}>Questions about these Terms can be sent to:</p>
      <ul className={ul}>
        <li>{COMPANY_LEGAL_NAME}</li>
        <li>{REGISTERED_ADDRESS}</li>
        <li>
          <a href={`mailto:${SUPPORT_EMAIL}`} className="text-accent-strong hover:text-accent-ink">{SUPPORT_EMAIL}</a>
          {' '}(general enquiries) or{' '}
          <a href={`mailto:${PRIVACY_EMAIL}`} className="text-accent-strong hover:text-accent-ink">{PRIVACY_EMAIL}</a>
          {' '}(privacy-related enquiries)
        </li>
      </ul>
    </>
  );
}
