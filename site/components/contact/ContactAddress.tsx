import {
  COMPANY_LEGAL_NAME,
  PHONE_NUMBER,
  REGISTERED_ADDRESS,
  RESPONSE_TIME_SLA,
  SUPPORT_EMAIL,
} from '@/lib/legal-placeholders';

export function ContactAddress({
  showPhone = false,
  showResponseTime = false,
  className,
}: {
  showPhone?: boolean;
  showResponseTime?: boolean;
  className?: string;
}) {
  return (
    <address className={`not-italic space-y-1 text-sm text-ink-muted ${className ?? ''}`.trim()}>
      <p className="font-medium text-ink">{COMPANY_LEGAL_NAME}</p>
      <p>{REGISTERED_ADDRESS}</p>
      <p>
        <a href={`mailto:${SUPPORT_EMAIL}`} className="hover:text-ink">
          {SUPPORT_EMAIL}
        </a>
      </p>
      {showPhone && <p>{PHONE_NUMBER}</p>}
      {showResponseTime && <p>Response time: {RESPONSE_TIME_SLA}</p>}
    </address>
  );
}
