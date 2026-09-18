'use client';

import { useEffect, useState } from 'react';
import { ActionIcon, Button, Group } from '@mantine/core';
import { IconDownload, IconX } from '@tabler/icons-react';

const DISMISSED_KEY = 'reelforge-install-dismissed';

// Chrome/Edge-only affordance for installing the dashboard as a PWA. Deliberately no iOS
// handling — Safari never fires `beforeinstallprompt`, so there's nothing to hook there, and
// building UA-sniffed "Add to Home Screen" instructions is out of scope for this pass.
interface BeforeInstallPromptEvent extends Event {
  prompt: () => Promise<void>;
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>;
}

function wasDismissed() {
  try {
    return localStorage.getItem(DISMISSED_KEY) === '1';
  } catch {
    return false;
  }
}

function markDismissed() {
  try {
    localStorage.setItem(DISMISSED_KEY, '1');
  } catch {
    // Ignore — worst case the prompt reappears next session.
  }
}

export function InstallPrompt() {
  const [deferredPrompt, setDeferredPrompt] = useState<BeforeInstallPromptEvent | null>(null);

  useEffect(() => {
    if (wasDismissed()) return;

    const handleBeforeInstallPrompt = (e: Event) => {
      e.preventDefault();
      setDeferredPrompt(e as BeforeInstallPromptEvent);
    };
    const handleAppInstalled = () => {
      setDeferredPrompt(null);
    };

    window.addEventListener('beforeinstallprompt', handleBeforeInstallPrompt);
    window.addEventListener('appinstalled', handleAppInstalled);
    return () => {
      window.removeEventListener('beforeinstallprompt', handleBeforeInstallPrompt);
      window.removeEventListener('appinstalled', handleAppInstalled);
    };
  }, []);

  if (!deferredPrompt) return null;

  const handleInstall = async () => {
    try {
      await deferredPrompt.prompt();
      await deferredPrompt.userChoice;
    } finally {
      setDeferredPrompt(null);
    }
  };

  const handleDismiss = () => {
    markDismissed();
    setDeferredPrompt(null);
  };

  return (
    <Group gap={4} visibleFrom="sm">
      <Button
        variant="light"
        color="violet"
        size="xs"
        leftSection={<IconDownload size={14} />}
        onClick={handleInstall}
      >
        Install app
      </Button>
      <ActionIcon variant="subtle" color="gray" size="sm" onClick={handleDismiss} aria-label="Dismiss install prompt">
        <IconX size={14} />
      </ActionIcon>
    </Group>
  );
}
