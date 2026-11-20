import { invitationTokenFromUrl, setPendingInvitation } from '@/lib/pendingInvitation';

/**
 * Rewrites the links the system hands to the app, before the router sees them
 * (Expo Router's `+native-intent`, run at launch with `initial: true` and for
 * each link received while the app runs).
 *
 * An invitation link (`uzu://invitation/<token>`) is not navigated to here: the
 * route sits behind the sign-in gate, which would drop it for a signed-out
 * person (or while the stored session is still being read). It is kept as the
 * pending invitation, and the signed-in area opens its screen once the person
 * is known (`src/app/(app)/_layout.tsx`). `null` leaves the navigation alone:
 * the app starts on its usual screen, or stays on the current one.
 *
 * Runs outside React and must never throw. Nothing is logged: the token is a secret.
 */
export function redirectSystemPath({ path }: { path: string; initial: boolean }): string | null {
  try {
    const token = invitationTokenFromUrl(path);
    if (token) {
      setPendingInvitation(token);
      return null;
    }
  } catch {
    // Fall through: an unreadable link is handed to the router as it came.
  }
  return path;
}
