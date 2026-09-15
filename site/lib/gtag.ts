// Thin, safe wrapper around `window.gtag`. No-ops when GA hasn't loaded —
// which is the case whenever consent isn't granted or the measurement ID
// env var is unset, since `GoogleAnalytics` never injects the script then.
export function trackEvent(name: string, params?: Record<string, unknown>): void {
  if (typeof window !== 'undefined' && window.gtag) {
    window.gtag('event', name, params);
  }
}
