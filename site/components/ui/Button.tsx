import Link from 'next/link';
import type { ReactNode } from 'react';
import { buttonClass, type ButtonSize, type ButtonVariant } from './button-styles';

interface ButtonProps {
  href: string;
  variant: ButtonVariant;
  size?: ButtonSize;
  block?: boolean;
  skew?: boolean;
  className?: string;
  children: ReactNode;
}

export function Button({ href, variant, size, block, skew, className, children }: ButtonProps) {
  const classes = `${buttonClass({ variant, size, block, skew })} ${className ?? ''}`;
  const content = skew ? <span className="inline-block skew-x-[5deg]">{children}</span> : children;

  // /app/* is a different Next.js app (the dashboard) reached only via nginx in production;
  // next/link would try a client-side navigation within this app's own router and 404.
  // http(s):// and mailto: are also always real page loads, never client-side routes.
  if (href.startsWith('/app') || href.startsWith('http') || href.startsWith('mailto:')) {
    return (
      <a href={href} className={classes}>
        {content}
      </a>
    );
  }

  return (
    <Link href={href} className={classes}>
      {content}
    </Link>
  );
}
