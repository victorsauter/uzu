import { useSyncExternalStore } from 'react';

import type { SessionUser } from '@/api/types';

/** Display name given by the API to an account created without a name (spec §1, lot 1). */
export const DEFAULT_DISPLAY_NAME = 'Nouveau membre';

const shown = new Set<string>();
let inProgress = false;
const listeners = new Set<() => void>();

function setInProgress(value: boolean) {
  if (inProgress === value) return;
  inProgress = value;
  listeners.forEach((listener) => listener());
}

/**
 * True once per user and app launch when the display name is still the API
 * default: the app then opens the edit-profile screen with a welcome sentence.
 * The welcome then counts as in progress until `endWelcome` (the screen left).
 */
export function shouldShowWelcome(user: SessionUser): boolean {
  if (user.displayName !== DEFAULT_DISPLAY_NAME || shown.has(user.id)) return false;
  shown.add(user.id);
  setInProgress(true);
  return true;
}

/** The welcome screen is gone, however it was left (saved, « Plus tard », swiped away). */
export function endWelcome(): void {
  setInProgress(false);
}

/** Whether the welcome screen is up now (read inside an effect, where a hook value may be one render late). */
export function isWelcomeInProgress(): boolean {
  return inProgress;
}

/** True while the welcome screen is up: anything else that wants the screen (a pending invitation) waits. */
export function useWelcomeInProgress(): boolean {
  return useSyncExternalStore(
    (listener) => {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    () => inProgress,
  );
}

/** Forgets what was shown (tests). */
export function resetWelcomeNudge(): void {
  shown.clear();
  setInProgress(false);
}
