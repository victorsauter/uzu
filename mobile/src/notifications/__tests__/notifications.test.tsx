import { act, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library';
import { Alert } from 'react-native';

import { sessionStore } from '@/auth/sessionStore';
import { config } from '@/lib/config';
import { t } from '@/strings';
import { authResponse, testDog, walkDetail } from '@/test/fixtures';
import { mockApi, ok } from '@/test/mockApi';

import { getPermissionState, offerNotifications, registerDevice, unregisterDevice } from '..';
import { targetForNotification } from '../routing';

type NotificationsMock = typeof import('expo-notifications') & {
  __reset(): void;
  __tap(data: unknown): void;
  __receive(data: unknown): void;
  __renewToken(token: string): void;
  __state: { permission: { granted: boolean; status: string; canAskAgain: boolean } };
};
// eslint-disable-next-line @typescript-eslint/no-require-imports -- the mock of jest.setup.ts
const notifications = require('expo-notifications') as NotificationsMock;

const APP_DIR = './src/app';
const walkId = 'a1a1a1a1-0000-4000-8000-000000000001';
const walkPath = `/api/v1/walks/${walkId}`;
const TOKEN = 'ExponentPushToken[test-token]';
const encodedToken = encodeURIComponent(TOKEN);

function grant() {
  notifications.__state.permission = { granted: true, status: 'granted', canAskAgain: true };
}

beforeEach(() => {
  notifications.__reset();
  jest.clearAllMocks();
  config.easProjectId = 'project-id';
});

afterEach(async () => {
  config.easProjectId = undefined;
  await act(() => sessionStore.clear());
  jest.restoreAllMocks();
});

describe('notification links', () => {
  it('opens the thread for a message and the walk detail for every other type', () => {
    expect(
      targetForNotification({ type: 'message_posted', walkId, url: `uzu://walks/${walkId}` })?.href,
    ).toEqual({ pathname: '/walks/[id]/messages', params: { id: walkId } });
    for (const type of [
      'registration_created',
      'registration_requested',
      'registration_accepted',
      'registration_declined',
      'registration_cancelled_by_owner',
      'walk_updated',
      'walk_cancelled',
      'walk_reminder',
      'series_walk_created',
    ]) {
      expect(targetForNotification({ type, walkId, url: `uzu://walks/${walkId}` })?.href).toEqual({
        pathname: '/walks/[id]',
        params: { id: walkId },
      });
    }
  });

  const organizationId = 'c0c0c0c0-0000-4000-8000-000000000001';
  const organizationUrl = `uzu://organizations/${organizationId}`;

  it('opens the members screen for a membership request, the organisation for the other types', () => {
    expect(
      targetForNotification({ type: 'membership_requested', organizationId, url: organizationUrl }),
    ).toEqual({
      organizationId,
      type: 'membership_requested',
      href: { pathname: '/organizations/[id]/members', params: { id: organizationId } },
    });
    for (const type of ['membership_accepted', 'organization_approved']) {
      expect(targetForNotification({ type, organizationId, url: organizationUrl })?.href).toEqual({
        pathname: '/organizations/[id]',
        params: { id: organizationId },
      });
    }
  });

  it('applies the same strict validation to organisation links', () => {
    const bad = [
      { url: 'https://evil.example/organizations/abc' },
      { url: 'uzu://evil/organizations/abc' },
      { url: 'uzu://organizations/abc/../../admin' },
      { url: 'uzu://organizations/abc?x=1' },
      { url: 'uzu://organizations/' },
      { url: 'uzu://organizations/abc/members' },
      { url: 'http://organizations/abc' },
      { type: 'membership_accepted', organizationId: 'other', url: 'uzu://organizations/abc' },
      // The type must agree with the kind of link.
      { type: 'membership_accepted', url: `uzu://walks/${walkId}` },
      { type: 'walk_updated', url: 'uzu://organizations/abc' },
    ];
    for (const data of bad) expect(targetForNotification(data)).toBeNull();
  });

  it('report_created (administrators, ADR 0036) has no link: it is ignored, even with one', () => {
    expect(targetForNotification({ type: 'report_created' })).toBeNull();
    expect(targetForNotification({ type: 'report_created', reportId: 'r1' })).toBeNull();
    expect(
      targetForNotification({ type: 'report_created', url: `uzu://walks/${walkId}` }),
    ).toBeNull();
    expect(
      targetForNotification({ type: 'report_created', url: 'uzu://admin/reports' }),
    ).toBeNull();
  });

  it('ignores anything that is not one of our own walk links', () => {
    const bad = [
      undefined,
      null,
      'uzu://walks/abc',
      {},
      { url: 42 },
      { url: 'https://evil.example/walks/abc' },
      // The former scheme (before the rename to Uzu, ADR 0041) is not followed any more.
      { url: 'dogo://walks/abc' },
      { type: 'walk_updated', walkId: 'abc', url: 'dogo://walks/abc' },
      { url: 'uzu://evil/walks/abc' },
      { url: 'uzu://walks/abc/../../admin' },
      { url: 'uzu://walks/abc?x=1' },
      { url: 'uzu://users/abc' },
      { url: 'http://walks/abc' },
      { url: 'uzu://walks/' },
      { type: 'walk_updated', walkId: 'other', url: 'uzu://walks/abc' },
    ];
    for (const data of bad) expect(targetForNotification(data)).toBeNull();
  });
});

describe('push unavailable (Expo Go, no EAS project)', () => {
  it('never loads the module nor calls the API, and says nothing', async () => {
    config.easProjectId = undefined;
    const { calls } = mockApi({});
    const consoleSpy = [
      jest.spyOn(console, 'error').mockImplementation(() => {}),
      jest.spyOn(console, 'warn').mockImplementation(() => {}),
    ];
    const alert = jest.spyOn(Alert, 'alert');

    await expect(getPermissionState()).resolves.toBe('unavailable');
    await registerDevice();
    await unregisterDevice();
    await offerNotifications();

    expect(calls).toHaveLength(0);
    expect(alert).not.toHaveBeenCalled();
    expect(notifications.getPermissionsAsync).not.toHaveBeenCalled();
    expect(notifications.setNotificationHandler).not.toHaveBeenCalled();
    consoleSpy.forEach((spy) => expect(spy).not.toHaveBeenCalled());
  });

  it('starts the app and signs in without any notification call', async () => {
    config.easProjectId = undefined;
    const { calls } = mockApi({ 'GET /api/v1/me/dogs': () => ok([]) });
    await sessionStore.set(authResponse());
    await renderRouter(APP_DIR, { initialUrl: '/profile' });
    expect(await screen.findByText(t.dog.myDogsEmptyTitle)).toBeTruthy();
    expect(calls.some((c) => c.path.includes('/me/devices'))).toBe(false);
    expect(notifications.addNotificationResponseReceivedListener).not.toHaveBeenCalled();
  });

  it('treats a failing token request as « unavailable », without an error', async () => {
    grant();
    mockApi({});
    jest.mocked(notifications.getExpoPushTokenAsync).mockRejectedValueOnce(new Error('no push'));
    const consoleSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
    const alert = jest.spyOn(Alert, 'alert');

    await expect(registerDevice()).resolves.toBeUndefined();

    expect(consoleSpy).not.toHaveBeenCalled();
    expect(alert).not.toHaveBeenCalled();
  });
});

describe('device registration', () => {
  it('registers the device after sign-in when permission is granted', async () => {
    grant();
    const { calls } = mockApi({
      'PUT /api/v1/me/devices': () => ok({ expoPushToken: TOKEN, platform: 'Ios', updatedAt: 'x' }),
      'GET /api/v1/me/dogs': () => ok([]),
    });
    await sessionStore.set(authResponse());
    await renderRouter(APP_DIR, { initialUrl: '/profile' });

    await waitFor(() => expect(calls.some((c) => c.method === 'PUT')).toBe(true));
    const put = calls.find((c) => c.method === 'PUT')!;
    expect(put.path).toBe('/api/v1/me/devices');
    expect(put.json).toEqual({ expoPushToken: TOKEN, platform: 'Ios' });
    expect(notifications.getExpoPushTokenAsync).toHaveBeenCalledWith({ projectId: 'project-id' });
  });

  it('registers the new token when the system rolls it, and removes the old one', async () => {
    grant();
    const NEW_TOKEN = 'ExponentPushToken[renewed-token]';
    const { calls } = mockApi({
      'PUT /api/v1/me/devices': () => ok({ expoPushToken: TOKEN, platform: 'Ios', updatedAt: 'x' }),
      [`DELETE /api/v1/me/devices/${encodedToken}`]: () => new Response(null, { status: 204 }),
      'GET /api/v1/me/dogs': () => ok([]),
    });
    await sessionStore.set(authResponse());
    await renderRouter(APP_DIR, { initialUrl: '/profile' });
    await waitFor(() => expect(calls.filter((c) => c.method === 'PUT')).toHaveLength(1));

    await act(async () => notifications.__renewToken(NEW_TOKEN));

    await waitFor(() => expect(calls.filter((c) => c.method === 'PUT')).toHaveLength(2));
    expect(calls.filter((c) => c.method === 'PUT')[1]!.json).toEqual({
      expoPushToken: NEW_TOKEN,
      platform: 'Ios',
    });
    await waitFor(() =>
      expect(calls.some((c) => c.method === 'DELETE' && c.path.endsWith(encodedToken))).toBe(true),
    );
  });

  it('ignores a renewed token while nobody is signed in', async () => {
    grant();
    const { calls } = mockApi({});
    await renderRouter(APP_DIR, { initialUrl: '/' });
    await screen.findByText(t.app.tagline);

    await act(async () => notifications.__renewToken('ExponentPushToken[renewed-token]'));
    expect(calls.some((c) => c.path.includes('/me/devices'))).toBe(false);
  });

  it('does not ask for permission nor register when it is not granted', async () => {
    const { calls } = mockApi({ 'GET /api/v1/me/dogs': () => ok([]) });
    await sessionStore.set(authResponse());
    await renderRouter(APP_DIR, { initialUrl: '/profile' });
    await screen.findByText(t.dog.myDogsEmptyTitle);

    expect(notifications.requestPermissionsAsync).not.toHaveBeenCalled();
    expect(calls.some((c) => c.path.includes('/me/devices'))).toBe(false);
  });

  it('removes the device (URL-encoded token) before the session is cleared', async () => {
    grant();
    let sessionAtDelete: unknown = 'unset';
    const { calls } = mockApi({
      'PUT /api/v1/me/devices': () => ok({ expoPushToken: TOKEN, platform: 'Ios', updatedAt: 'x' }),
      'GET /api/v1/me/dogs': () => ok([]),
    });
    const originalFetch = globalThis.fetch;
    globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
      const request = input instanceof Request ? input : new Request(input, init);
      if (request.method === 'DELETE') {
        sessionAtDelete = sessionStore.getSession();
        return new Response(null, { status: 204 });
      }
      return originalFetch(input, init);
    }) as typeof fetch;

    await sessionStore.set(authResponse());
    await renderRouter(APP_DIR, { initialUrl: '/profile' });
    await waitFor(() => expect(calls.some((c) => c.method === 'PUT')).toBe(true));

    await fireEvent.press(await screen.findByRole('button', { name: t.auth.signOut }));
    await waitFor(() => expect(sessionAtDelete).not.toBe('unset'));
    expect(sessionAtDelete).not.toBeNull();
    await waitFor(() => expect(sessionStore.getSession()).toBeNull());
  });

  it('builds the DELETE address with the encoded token', async () => {
    grant();
    const { calls } = mockApi({
      'DELETE /api/v1/me/devices/ExponentPushToken%5Btest-token%5D': () =>
        new Response(null, { status: 204 }),
    });
    await sessionStore.set(authResponse());
    await unregisterDevice();
    expect(calls).toHaveLength(1);
    expect(calls[0]!.method).toBe('DELETE');
    expect(calls[0]!.request.url).toContain(`/api/v1/me/devices/${encodedToken}`);
  });

  it('signs out even when the removal fails', async () => {
    grant();
    mockApi({
      'GET /api/v1/me/dogs': () => ok([]),
      'DELETE /api/v1/me/devices/ExponentPushToken%5Btest-token%5D': () =>
        new Response('boom', { status: 500 }),
    });
    await sessionStore.set(authResponse());
    await renderRouter(APP_DIR, { initialUrl: '/profile' });
    await fireEvent.press(await screen.findByRole('button', { name: t.auth.signOut }));
    await waitFor(() => expect(sessionStore.getSession()).toBeNull());
  });
});

describe('asking for permission', () => {
  it('explains first, then asks, once', async () => {
    const { calls } = mockApi({
      'PUT /api/v1/me/devices': () => ok({ expoPushToken: TOKEN, platform: 'Ios', updatedAt: 'x' }),
    });
    await sessionStore.set(authResponse());
    const alert = jest.spyOn(Alert, 'alert').mockImplementation(() => {});

    await offerNotifications();
    expect(alert).toHaveBeenCalledWith(
      t.notifications.title,
      t.permissions.notifications,
      expect.any(Array),
    );
    expect(notifications.requestPermissionsAsync).not.toHaveBeenCalled();

    const buttons = alert.mock.calls[0]![2] as { text: string; onPress?: () => void }[];
    await act(async () => buttons.find((b) => b.text === t.notifications.allow)!.onPress!());
    expect(notifications.requestPermissionsAsync).toHaveBeenCalled();
    await waitFor(() => expect(calls.some((c) => c.method === 'PUT')).toBe(true));

    await offerNotifications();
    expect(alert).toHaveBeenCalledTimes(1);
  });

  it('remembers that it asked, even when the answer was « Plus tard »', async () => {
    mockApi({});
    const alert = jest.spyOn(Alert, 'alert').mockImplementation(() => {});
    await offerNotifications();
    await offerNotifications();
    expect(alert).toHaveBeenCalledTimes(1);
    expect(notifications.requestPermissionsAsync).not.toHaveBeenCalled();
  });

  it('does not ask when the permission is already decided', async () => {
    grant();
    mockApi({});
    const alert = jest.spyOn(Alert, 'alert').mockImplementation(() => {});
    await offerNotifications();
    expect(alert).not.toHaveBeenCalled();
  });

  it('is offered after registering to a walk', async () => {
    const dog = testDog;
    mockApi({
      [`GET ${walkPath}`]: () => ok(walkDetail()),
      'GET /api/v1/me/dogs': () => ok([dog]),
      [`POST ${walkPath}/registrations`]: () =>
        ok([{ id: 'r1', walkId, dogId: dog.id, status: 'Accepted' }]),
    });
    const alert = jest.spyOn(Alert, 'alert').mockImplementation(() => {});
    await sessionStore.set(authResponse());
    await renderRouter(APP_DIR, { initialUrl: `/walks/${walkId}` });
    await fireEvent.press(await screen.findByRole('checkbox', { name: dog.name }));
    await fireEvent.press(screen.getByRole('button', { name: /^Inscrire/ }));
    await waitFor(() =>
      expect(alert).toHaveBeenCalledWith(
        t.notifications.title,
        t.permissions.notifications,
        expect.any(Array),
      ),
    );
  });
});

describe('receiving and tapping notifications', () => {
  const routes = {
    [`GET ${walkPath}`]: () => ok(walkDetail({ isOrganizer: true })),
    [`GET ${walkPath}/messages`]: () => ok({ hasMore: false, items: [] }),
    'GET /api/v1/me/dogs': () => ok([]),
  };

  async function start() {
    await sessionStore.set(authResponse());
    await renderRouter(APP_DIR, { initialUrl: '/profile' });
    await screen.findByText(t.dog.myDogsEmptyTitle);
  }

  it('opens the walk detail when the app is running', async () => {
    mockApi(routes);
    await start();
    await act(async () =>
      notifications.__tap({ type: 'walk_updated', walkId, url: `uzu://walks/${walkId}` }),
    );
    await waitFor(() => expect(screen).toHavePathname(`/walks/${walkId}`));
  });

  it('opens the thread for a message', async () => {
    mockApi(routes);
    await start();
    await act(async () =>
      notifications.__tap({ type: 'message_posted', walkId, url: `uzu://walks/${walkId}` }),
    );
    await waitFor(() => expect(screen).toHavePathname(`/walks/${walkId}/messages`));
  });

  it('opens the right screen when the app was closed', async () => {
    mockApi(routes);
    (notifications as unknown as { __state: { last: unknown } }).__state.last = {
      notification: {
        request: {
          content: {
            data: { type: 'message_posted', walkId, url: `uzu://walks/${walkId}` },
          },
        },
      },
    };
    await sessionStore.set(authResponse());
    await renderRouter(APP_DIR, { initialUrl: '/profile' });
    await waitFor(() => expect(screen).toHavePathname(`/walks/${walkId}/messages`));
    expect(notifications.clearLastNotificationResponse).toHaveBeenCalled();
  });

  it.each([
    ['membership_requested', '/members'],
    ['membership_accepted', ''],
    ['organization_approved', ''],
  ])('opens the organisation for %s', async (type, suffix) => {
    const organizationId = 'c0c0c0c0-0000-4000-8000-000000000001';
    mockApi({
      ...routes,
      [`GET /api/v1/organizations/${organizationId}`]: () =>
        ok({
          id: organizationId,
          name: 'Club canin du lac',
          description: '',
          city: null,
          status: 'Approved',
          createdAt: '2026-11-01T10:00:00Z',
          memberCount: 1,
          educators: [],
          myMembership: { id: 'm', role: 'Owner', status: 'Accepted' },
          upcomingWalks: [],
        }),
      [`GET /api/v1/organizations/${organizationId}/memberships`]: () =>
        ok({ members: [], requests: [] }),
    });
    await start();
    await act(async () =>
      notifications.__tap({
        type,
        organizationId,
        url: `uzu://organizations/${organizationId}`,
      }),
    );
    await waitFor(() => expect(screen).toHavePathname(`/organizations/${organizationId}${suffix}`));
  });

  it('receives and taps report_created without navigating or failing (administrators)', async () => {
    const { calls } = mockApi({ ...routes, 'GET /api/v1/me/organizations': () => ok([]) });
    const consoleSpy = [
      jest.spyOn(console, 'error').mockImplementation(() => {}),
      jest.spyOn(console, 'warn').mockImplementation(() => {}),
    ];
    await start();
    const before = calls.length;

    await act(async () => {
      notifications.__receive({ type: 'report_created' });
      notifications.__tap({ type: 'report_created' });
    });
    // Let the refresh started by the reception settle.
    await waitFor(() => expect(calls.length).toBeGreaterThanOrEqual(before));

    expect(screen).toHavePathname('/profile');
    expect(screen.queryByText(t.states.errorTitle)).toBeNull();
    expect(screen.queryByText(t.states.crashTitle)).toBeNull();
    expect(notifications.clearLastNotificationResponse).toHaveBeenCalled();
    consoleSpy.forEach((spy) => expect(spy).not.toHaveBeenCalled());
  });

  it('ignores an organisation link carrying a walk type', async () => {
    mockApi(routes);
    await start();
    await act(async () =>
      notifications.__tap({ type: 'walk_updated', url: 'uzu://organizations/abc' }),
    );
    expect(screen).toHavePathname('/profile');
  });

  it('ignores an unknown or foreign link', async () => {
    mockApi(routes);
    await start();
    await act(async () => {
      notifications.__tap({ type: 'walk_updated', url: 'https://evil.example/walks/1' });
      notifications.__tap({ type: 'walk_updated', url: `https://uzu.example/walks/${walkId}` });
      notifications.__tap({ type: 'walk_updated', walkId, url: `dogo://walks/${walkId}` });
      notifications.__tap({ type: 'walk_updated', url: 'uzu://admin' });
      notifications.__tap({});
    });
    expect(screen).toHavePathname('/profile');
  });

  /** A negative check: the walk the tap pointed to never opens, however long we wait. */
  async function expectToStayOnTheHomeScreen() {
    await expect(
      waitFor(() => expect(screen).toHavePathname(`/walks/${walkId}`), { timeout: 400 }),
    ).rejects.toBeDefined();
    expect(screen).toHavePathname('/');
  }

  it('forgets a tap made while signed out: it may belong to another account', async () => {
    mockApi(routes);
    await renderRouter(APP_DIR, { initialUrl: '/' });
    await screen.findByText(t.app.tagline);

    // Account A's notification is tapped while nobody is signed in…
    await act(async () =>
      notifications.__tap({ type: 'walk_updated', walkId, url: `uzu://walks/${walkId}` }),
    );
    // …then account B signs in: A's walk must not open.
    await act(() => sessionStore.set(authResponse()));
    expect(await screen.findByLabelText(t.discover.mapLabel)).toBeTruthy();
    await expectToStayOnTheHomeScreen();
  });

  it('forgets a tap that waited for the session when it turns out nobody is signed in', async () => {
    mockApi(routes);
    (notifications as unknown as { __state: { last: unknown } }).__state.last = {
      notification: {
        request: {
          content: {
            data: { type: 'walk_updated', walkId, url: `uzu://walks/${walkId}` },
          },
        },
      },
    };
    await renderRouter(APP_DIR, { initialUrl: '/' });
    await screen.findByText(t.app.tagline);

    await act(() => sessionStore.set(authResponse({ user: { displayName: 'Autre compte' } })));
    expect(await screen.findByLabelText(t.discover.mapLabel)).toBeTruthy();
    await expectToStayOnTheHomeScreen();
  });

  it('shows foreground notifications and refreshes the walks', async () => {
    const { calls } = mockApi(routes);
    await start();
    const handler = jest.mocked(notifications.setNotificationHandler).mock.calls[0]![0]!;
    await expect(handler.handleNotification({} as never)).resolves.toMatchObject({
      shouldShowBanner: true,
      shouldShowList: true,
    });

    await act(async () => notifications.__tap({ url: `uzu://walks/${walkId}` }));
    await waitFor(() => expect(screen).toHavePathname(`/walks/${walkId}`));
    const before = calls.filter((c) => c.path === walkPath).length;
    await act(async () => notifications.__receive({ type: 'walk_updated', walkId }));
    await waitFor(() =>
      expect(calls.filter((c) => c.path === walkPath).length).toBeGreaterThan(before),
    );
  });
});
