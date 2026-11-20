import { useSyncExternalStore } from 'react';

/**
 * The invitation link (`uzu://invitation/<token>`) that arrived while the person
 * could not open it yet: signed out, or the stored session still being read.
 * It waits in memory, for one invitation at a time (the last link wins), until
 * the signed-in area opens the screen (`takePendingInvitation`).
 *
 * The token is a secret (whoever holds it can join the dog's humans): it is
 * never displayed, logged or written to the device. It is forgotten when a
 * session ends (`clearPendingInvitation`, from the session provider).
 */
let pending: string | null = null;
const listeners = new Set<() => void>();

function emit() {
  listeners.forEach((listener) => listener());
}

// Only what the API produces (base64url) is accepted; anything longer or stranger is not a token.
const INVITATION_LINK = /(?:^|\/)invitation\/([A-Za-z0-9_-]{1,128})\/?(?:[?#].*)?$/;

/**
 * The token of an invitation link, or `null` for any other address. Accepts the
 * app link (`uzu://invitation/<token>`), the path alone (`/invitation/<token>`)
 * and the Expo Go form (`exp://host:8081/--/invitation/<token>`).
 */
export function invitationTokenFromUrl(url: string): string | null {
  return INVITATION_LINK.exec(url.trim())?.[1] ?? null;
}

/** Keeps `token` for after the sign-in (replacing the one kept before, if any). */
export function setPendingInvitation(token: string): void {
  pending = token;
  emit();
}

/** Returns the kept token and forgets it: an invitation link is opened once. */
export function takePendingInvitation(): string | null {
  const token = pending;
  if (token === null) return null;
  pending = null;
  emit();
  return token;
}

/** Whether a link is waiting, without taking it. */
export function hasPendingInvitation(): boolean {
  return pending !== null;
}

/** Forgets the kept token (end of a session, tests). */
export function clearPendingInvitation(): void {
  if (pending === null) return;
  pending = null;
  emit();
}

/** Re-renders when a link starts or stops waiting; the token itself is never given out, so it cannot end up on screen. */
export function usePendingInvitation(): boolean {
  return useSyncExternalStore(
    (listener) => {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    () => pending !== null,
  );
}
