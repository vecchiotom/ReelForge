'use client';

import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';

export type ConsentValue = 'granted' | 'denied' | null;

const STORAGE_KEY = 'rf-cookie-consent';

interface CookieConsentContextValue {
  consent: ConsentValue;
  accept: () => void;
  reject: () => void;
  reopen: () => void;
}

const CookieConsentContext = createContext<CookieConsentContextValue | undefined>(undefined);

function readStoredConsent(): ConsentValue {
  try {
    const stored = window.localStorage.getItem(STORAGE_KEY);
    return stored === 'granted' || stored === 'denied' ? stored : null;
  } catch {
    // localStorage unavailable (private browsing, disabled storage, etc.) — treat as no choice.
    return null;
  }
}

export function CookieConsentProvider({ children }: { children: ReactNode }) {
  // Default is `null` both on the server and on the very first client render, so there is no
  // hydration mismatch — the real value (if any) is only read after mount, inside an effect.
  const [consent, setConsent] = useState<ConsentValue>(null);

  useEffect(() => {
    setConsent(readStoredConsent());
  }, []);

  function persist(value: 'granted' | 'denied') {
    setConsent(value);
    try {
      window.localStorage.setItem(STORAGE_KEY, value);
    } catch {
      // Ignore write failures — state is still updated for this session.
    }
  }

  function accept() {
    persist('granted');
  }

  function reject() {
    persist('denied');
  }

  function reopen() {
    try {
      window.localStorage.removeItem(STORAGE_KEY);
    } catch {
      // Ignore — resetting in-memory state below still reopens the banner for this session.
    }
    setConsent(null);
  }

  return (
    <CookieConsentContext.Provider value={{ consent, accept, reject, reopen }}>
      {children}
    </CookieConsentContext.Provider>
  );
}

export function useCookieConsent(): CookieConsentContextValue {
  const context = useContext(CookieConsentContext);
  if (!context) {
    throw new Error('useCookieConsent must be used within a CookieConsentProvider');
  }
  return context;
}
