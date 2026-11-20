import { act, fireEvent, renderRouter, screen } from 'expo-router/testing-library';

import { sessionStore } from '@/auth/sessionStore';
import { hasPendingInvitation } from '@/lib/pendingInvitation';
import { t } from '@/strings';
import { authResponse } from '@/test/fixtures';

// The real route tree (src/app), with the real providers and the app session store.
const APP_DIR = './src/app';

// Sign-out revokes the refresh token on the API: keep the network out of tests.
const fetchMock = jest.fn(async () => new Response(null, { status: 204 }));
beforeAll(() => {
  globalThis.fetch = fetchMock as unknown as typeof fetch;
});

afterEach(async () => {
  await act(() => sessionStore.clear());
});

describe('auth gate', () => {
  it('shows the sign-in screen when no session is stored', async () => {
    await renderRouter(APP_DIR, { initialUrl: '/' });

    expect(await screen.findByText(t.app.tagline)).toBeTruthy();
    expect(screen).toHavePathname('/sign-in');
    expect(screen.queryByLabelText(t.discover.mapLabel)).toBeNull();
  });

  it('shows the tabs to a signed-in user, and the sign-in screen after signing out', async () => {
    await sessionStore.set(authResponse());
    await renderRouter(APP_DIR, { initialUrl: '/' });

    expect(await screen.findByLabelText(t.discover.mapLabel)).toBeTruthy();
    expect(screen).toHavePathname('/');

    await fireEvent.press(screen.getByText(t.tabs.profile));
    expect(await screen.findByText('Bonjour Léa Martin !')).toBeTruthy();

    await fireEvent.press(screen.getByRole('button', { name: t.auth.signOut }));

    expect(await screen.findByText(t.app.tagline)).toBeTruthy();
    expect(screen).toHavePathname('/sign-in');
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringMatching(/\/api\/v1\/auth\/logout$/),
      expect.objectContaining({ method: 'POST' }),
    );
  });

  it('keeps signed-out users away from app routes', async () => {
    await renderRouter(APP_DIR, { initialUrl: '/profile' });
    expect(await screen.findByText(t.app.tagline)).toBeTruthy();
    expect(screen).toHavePathname('/sign-in');
  });

  // The link is kept for after the sign-in (see pending-invitation.test.tsx for the whole journey).
  it('keeps signed-out users away from an invitation link, which does not reach the API', async () => {
    await renderRouter(APP_DIR, { initialUrl: '/invitation/some-token' });
    expect(await screen.findByText(t.app.tagline)).toBeTruthy();
    expect(screen).toHavePathname('/sign-in');
    expect(fetchMock).not.toHaveBeenCalledWith(
      expect.stringContaining('dog-invitations'),
      expect.anything(),
    );
    expect(hasPendingInvitation()).toBe(true);
  });
});
