interface RegistrationMarkProps {
  className?: string;
}

/** Decorative accent square. Caller positions it with `absolute` + offset classes via `className`. */
export function RegistrationMark({ className }: RegistrationMarkProps) {
  return <div aria-hidden="true" className={`h-2 w-2 bg-accent ${className ?? ''}`} />;
}
