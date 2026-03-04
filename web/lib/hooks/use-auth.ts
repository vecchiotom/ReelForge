'use client';

import { createContext, useContext, useCallback, useMemo, useSyncExternalStore } from 'react';
import type { UserInfo } from '../types/auth';

function getUserFromCookie(): UserInfo | null {
  if (typeof document === 'undefined') return null;
  const match = document.cookie.match(/reelforge_user=([^;]+)/);
  if (!match) return null;
  try {
    return JSON.parse(decodeURIComponent(match[1]));
  } catch {
    return null;
  }
}



let cachedUser: UserInfo | null = null;
let listeners: (() => void)[] = [];

function subscribe(listener: () => void) {
  listeners.push(listener);
  return () => {
    listeners = listeners.filter((l) => l !== listener);
  };
}

function getSnapshot(): UserInfo | null {
  const user = getUserFromCookie();
  if (JSON.stringify(user) !== JSON.stringify(cachedUser)) {
    cachedUser = user;
  }
  return cachedUser;
}

function getServerSnapshot(): UserInfo | null {
  return null;
}

export function setUserCookie(user: UserInfo) {
  document.cookie = `reelforge_user=${encodeURIComponent(JSON.stringify(user))}; path=/; SameSite=Lax`;
}

export function notifyAuthChange() {
  cachedUser = getUserFromCookie();
  listeners.forEach((l) => l());
}

export function useAuth() {
  const user = useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot);

  const logout = useCallback(async () => {
    await fetch('/api/auth/logout', { method: 'POST' });
    notifyAuthChange();
    window.location.href = '/login';
  }, []);

  return useMemo(
    () => ({
      user,
      isAuthenticated: !!user,
      isAdmin: user?.isAdmin ?? false,
      logout,
    }),
    [user, logout],
  );
}
