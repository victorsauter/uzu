import { act, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library';

import { redirectSystemPath } from '@/app/+native-intent';
import { sessionStore } from '@/auth/sessionStore';
import { hasPendingInvitation } from '@/lib/pendingInvitation';
import { resetWelcomeNudge } from '@/lib/welcomeNudge';
import { t } from '@/strings';
import { authResponse, testDog, testUser } from '@/test/fixtures';
import { mockApi, ok } from '@/test/mockApi';

// The real route tree (src/app), with the real providers and the app session store.
const APP_DIR = './src/app';

const token = 'tok-123';
const previewPath = `/api/v1/dog-invitations/${token}`;
const camille = {
  id: '6f1d3c1e-0000-4000-8000-0000000000c1',
  displayName: 'Camille',
  photoUrl: null,
};
const invitedTitle = 'Camille t’invite à rejoindre Biscotte';
const newMember = { displayName: 'Nouveau membre' };

const preview = {
  dog: { ...testDog, members: [{ user: camille, role: 'Owner' }] },
  invitedBy: camille,
  role: 'Owner',
  expiresAt: new Date(Date.now() + 6 * 86_400_000).toISOString(),
};

function mockServer(profile: object = testUser, dogs: object[] = []) {
  return mockApi({
    'GET /api/v1/me': () => ok(profile),
    'GET /api/v1/me/dogs': () => ok(dogs),
    'PATCH /api/v1/me': () => ok({ ...testUser, displayName: 'Léa' }),
    [`GET ${previewPath}`]: () => ok(preview),
  });
}

const previewCalls = (calls: { path: string }[]) =>
  calls.filter((call) => call.path === previewPath).length;

beforeEach(() => resetWelcomeNudge());

afterEach(async () => {
  await act(() => sessionStore.clear());
  jest.restoreAllMocks();
});

describe('an invitation link opened before signing in', () => {
  it('opens the invitation once the person is signed in', async () => {
    const { calls } = mockServer();
    await renderRouter(APP_DIR, { initialUrl: `/invitation/${token}` });

    expect(await screen.findByText(t.app.tagline)).toBeTruthy();
    expect(screen).toHavePathname('/sign-in');
    expect(previewCalls(calls)).toBe(0);

    await act(() => sessionStore.set(authResponse()));

    expect(await screen.findByText(invitedTitle)).toBeTruthy();
    expect(screen).toHavePathname(`/invitation/${token}`);
    expect(previewCalls(calls)).toBe(1);
    expect(hasPendingInvitation()).toBe(false);
  });

  it('opens it only once: « Plus tard » does not bring it back', async () => {
    mockServer();
    await renderRouter(APP_DIR, { initialUrl: `/invitation/${token}` });
    await screen.findByText(t.app.tagline);
    await act(() => sessionStore.set(authResponse()));
    await fireEvent.press(await screen.findByRole('button', { name: t.dogInvitation.later }));
    await waitFor(() => expect(screen).toHavePathname('/'));

    // Neither a profile refresh nor a later sign-in reopens it.
    await act(() => sessionStore.updateUser({ ...testUser, bio: 'x' }));
    await act(() => sessionStore.clear());
    await act(() => sessionStore.set(authResponse()));
    expect(await screen.findByLabelText(t.discover.mapLabel)).toBeTruthy();
    expect(screen).toHavePathname('/');
  });

  it('is forgotten when a signed-in session ends', async () => {
    mockServer(testUser);
    await renderRouter(APP_DIR, { initialUrl: '/' });
    await act(() => sessionStore.set(authResponse({ user: newMember })));
    // The welcome screen is up: the link arrived meanwhile and waits behind it.
    await screen.findByText(t.profile.welcome);
    await act(async () => {
      expect(redirectSystemPath({ path: `uzu://invitation/${token}`, initial: false })).toBeNull();
    });
    expect(hasPendingInvitation()).toBe(true);

    await act(() => sessionStore.clear());
    expect(hasPendingInvitation()).toBe(false);

    // Whoever signs in next gets the app, not the previous person's invitation.
    await act(() => sessionStore.set(authResponse()));
    expect(await screen.findByLabelText(t.discover.mapLabel)).toBeTruthy();
    expect(screen).toHavePathname('/');
  });

  it('survives the stored session being read (cold start), then opens for a signed-in member', async () => {
    mockServer();
    await sessionStore.set(authResponse());
    await renderRouter(APP_DIR, { initialUrl: `/invitation/${token}` });

    expect(await screen.findByText(invitedTitle)).toBeTruthy();
    expect(screen).toHavePathname(`/invitation/${token}`);
  });

  describe('a new member', () => {
    it('fills in their profile, then sees the invitation rather than « Ajoute ton premier chien »', async () => {
      const { calls } = mockServer(newMember);
      await renderRouter(APP_DIR, { initialUrl: `/invitation/${token}` });
      await screen.findByText(t.app.tagline);

      await act(() => sessionStore.set(authResponse({ user: newMember })));

      // The profile comes first, and the invitation is not read yet.
      expect(await screen.findByText(t.profile.welcome)).toBeTruthy();
      expect(screen).toHavePathname('/edit-profile');
      expect(previewCalls(calls)).toBe(0);

      await fireEvent.changeText(screen.getByLabelText(t.profile.displayName), 'Léa');
      await fireEvent.press(screen.getByRole('button', { name: t.common.save }));

      expect(await screen.findByText(invitedTitle)).toBeTruthy();
      expect(screen).toHavePathname(`/invitation/${token}`);
      expect(screen.queryByText(t.dog.welcome)).toBeNull();
      expect(hasPendingInvitation()).toBe(false);

      // « Plus tard » on the invitation leaves it, with nothing to bring it back.
      await fireEvent.press(screen.getByRole('button', { name: t.dogInvitation.later }));
      await waitFor(() => expect(screen).not.toHavePathname(`/invitation/${token}`));
      expect(screen.queryByText(t.dog.welcome)).toBeNull();
      expect(previewCalls(calls)).toBe(1);
    });

    it('still gets the invitation when they skip the profile with « Plus tard »', async () => {
      mockServer(newMember);
      await renderRouter(APP_DIR, { initialUrl: `/invitation/${token}` });
      await screen.findByText(t.app.tagline);
      await act(() => sessionStore.set(authResponse({ user: newMember })));

      await fireEvent.press(await screen.findByRole('button', { name: t.profile.later }));

      expect(await screen.findByText(invitedTitle)).toBeTruthy();
      expect(screen).toHavePathname(`/invitation/${token}`);
    });

    it('without any invitation, is still asked for a first dog', async () => {
      mockServer(newMember);
      await sessionStore.set(authResponse({ user: newMember }));
      await renderRouter(APP_DIR, { initialUrl: '/' });
      await screen.findByText(t.profile.welcome);

      await fireEvent.changeText(screen.getByLabelText(t.profile.displayName), 'Léa');
      await fireEvent.press(screen.getByRole('button', { name: t.common.save }));

      expect(await screen.findByText(t.dog.welcome)).toBeTruthy();
      expect(screen).toHavePathname('/dogs/new');
    });
  });
});

describe('an invitation link received while the app runs', () => {
  it('opens the invitation over the current screen, for a signed-in member', async () => {
    mockServer();
    await sessionStore.set(authResponse());
    await renderRouter(APP_DIR, { initialUrl: '/' });
    await screen.findByLabelText(t.discover.mapLabel);

    await act(async () => {
      expect(redirectSystemPath({ path: `uzu://invitation/${token}`, initial: false })).toBeNull();
    });

    expect(await screen.findByText(invitedTitle)).toBeTruthy();
    expect(screen).toHavePathname(`/invitation/${token}`);
  });
});

describe('redirectSystemPath', () => {
  it('keeps an invitation link to itself and leaves every other link to the router', () => {
    expect(redirectSystemPath({ path: 'uzu://walks/abc', initial: true })).toBe('uzu://walks/abc');
    expect(redirectSystemPath({ path: '/profile', initial: false })).toBe('/profile');
    expect(hasPendingInvitation()).toBe(false);

    expect(redirectSystemPath({ path: `uzu://invitation/${token}`, initial: true })).toBeNull();
    expect(hasPendingInvitation()).toBe(true);
  });
});
