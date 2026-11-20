import { act, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library';
import { Alert, Share } from 'react-native';

import type { DogMember, UserSummary } from '@/api/types';

import { sessionStore } from '@/auth/sessionStore';
import { formatDate } from '@/lib/dates';
import { format, t } from '@/strings';
import { authResponse, testDog, testUser, walkDetail } from '@/test/fixtures';
import { mockApi, ok, problemResponse } from '@/test/mockApi';

const APP_DIR = './src/app';
const dogPath = `/api/v1/dogs/${testDog.id}`;
const camille: UserSummary = {
  id: '6f1d3c1e-0000-4000-8000-0000000000c1',
  displayName: 'Camille',
  photoUrl: null,
};
const victor: UserSummary = {
  id: '6f1d3c1e-0000-4000-8000-0000000000c2',
  displayName: 'Victor',
  photoUrl: null,
};
const member = (user: UserSummary): DogMember => ({ user, role: 'Owner' });
const noContent = () => new Response(null, { status: 204 });

afterEach(async () => {
  await act(() => sessionStore.clear());
  jest.restoreAllMocks();
});

async function signIn() {
  await sessionStore.set(authResponse());
}

/** Presses the button of the last `Alert.alert` confirmation that has this label. */
async function pressAlertButton(alert: jest.SpyInstance, text: string) {
  await waitFor(() => expect(alert).toHaveBeenCalled());
  const buttons = alert.mock.calls.at(-1)![2] as { text: string; onPress?: () => void }[];
  await act(async () => buttons.find((b) => b.text === text)!.onPress?.());
}

const inDays = (days: number) => new Date(Date.now() + days * 86_400_000).toISOString();

describe('« Ajouter un humain »', () => {
  async function open(members: DogMember[], invitations: object[] = []) {
    const { calls } = mockApi({
      [`GET ${dogPath}`]: () => ok({ ...testDog, members }),
      [`GET ${dogPath}/invitations`]: () => ok(invitations),
    });
    await signIn();
    await renderRouter(APP_DIR, { initialUrl: `/dogs/${testDog.id}` });
    await screen.findByText('Biscotte');
    return calls;
  }

  it('is the last line of « Ses humains » for an owner, and opens the sheet', async () => {
    await open([member(testUser)]);
    await fireEvent.press(
      await screen.findByRole('button', { name: 'Ajouter un humain à Biscotte' }),
    );
    expect(screen).toHavePathname(`/dogs/${testDog.id}/invite`);
    expect(await screen.findByText('Ajouter un humain à Biscotte')).toBeTruthy();
  });

  it('is absent when I am not one of the owners (and the invitations are not even asked)', async () => {
    const calls = await open([member(camille)]);
    expect(screen.queryByText(t.dog.addHuman)).toBeNull();
    expect(calls.some((call) => call.path === `${dogPath}/invitations`)).toBe(false);
  });

  describe('pending invitations', () => {
    const expiresAt = inDays(5);
    const invitation = {
      id: 'inv-1',
      role: 'Owner',
      createdBy: null,
      expiresAt,
      createdAt: inDays(-2),
    };

    it('shows one discreet line per invitation, with its expiry', async () => {
      await open(
        [member(testUser)],
        [invitation, { ...invitation, id: 'inv-2', expiresAt: inDays(6) }],
      );
      expect(
        await screen.findByText(format(t.dog.pendingInvitation, { date: formatDate(expiresAt) })),
      ).toBeTruthy();
      expect(screen.getAllByText(t.dog.cancelInvitation)).toHaveLength(2);
    });

    it('shows none when there is no pending invitation', async () => {
      await open([member(testUser)]);
      expect(screen.queryByText(t.dog.cancelInvitation)).toBeNull();
    });

    it('cancels one after confirmation, then reads the list again', async () => {
      const alert = jest.spyOn(Alert, 'alert').mockImplementation(() => {});
      let pending = [invitation];
      const { calls } = mockApi({
        [`GET ${dogPath}`]: () => ok(testDog),
        [`GET ${dogPath}/invitations`]: () => ok(pending),
        [`DELETE ${dogPath}/invitations/inv-1`]: () => {
          pending = [];
          return noContent();
        },
      });
      await signIn();
      await renderRouter(APP_DIR, { initialUrl: `/dogs/${testDog.id}` });
      await fireEvent.press(
        await screen.findByRole('button', { name: new RegExp(`^${t.dog.cancelInvitation}`) }),
      );
      expect(alert.mock.calls[0]![0]).toBe(t.dog.cancelInvitationTitle);
      expect(calls.some((call) => call.method === 'DELETE')).toBe(false);

      await pressAlertButton(alert, t.dog.cancelInvitation);
      await waitFor(() => expect(screen.queryByText(t.dog.cancelInvitation)).toBeNull());
      expect(calls.some((call) => call.method === 'DELETE')).toBe(true);
    });

    it('keeps the invitation when the confirmation is dismissed', async () => {
      const alert = jest.spyOn(Alert, 'alert').mockImplementation(() => {});
      const calls = await open([member(testUser)], [invitation]);
      await fireEvent.press(
        await screen.findByRole('button', { name: new RegExp(`^${t.dog.cancelInvitation}`) }),
      );
      await pressAlertButton(alert, t.dog.cancelInvitationKeep);
      expect(calls.some((call) => call.method === 'DELETE')).toBe(false);
    });
  });
});

describe('the invitation sheet', () => {
  const created = {
    id: 'inv-1',
    dogId: testDog.id,
    role: 'Owner',
    token: 'abc123',
    link: 'uzu://invitation/abc123',
    expiresAt: inDays(7),
    createdAt: new Date().toISOString(),
  };

  async function open(routes: Record<string, () => Response> = {}) {
    const result = mockApi({
      [`GET ${dogPath}`]: () => ok(testDog),
      [`GET ${dogPath}/invitations`]: () => ok([]),
      [`POST ${dogPath}/invitations`]: () => jsonCreated(created),
      ...routes,
    });
    await signIn();
    // Opened from the dog's profile, as in the app (a modal is never the first screen).
    await renderRouter(APP_DIR, { initialUrl: `/dogs/${testDog.id}` });
    await fireEvent.press(
      await screen.findByRole('button', { name: 'Ajouter un humain à Biscotte' }),
    );
    await screen.findByText('Ajouter un humain à Biscotte');
    return result;
  }
  const jsonCreated = (body: object) =>
    new Response(JSON.stringify(body), {
      status: 201,
      headers: { 'Content-Type': 'application/json' },
    });

  it('offers the three roles: only « Propriétaire » is selectable, the others say « Bientôt »', async () => {
    await open();
    expect(screen.getByText(t.dogInvitation.roleTitle)).toBeTruthy();
    const owner = screen.getByRole('radio', { name: /^Propriétaire\. Modifie Biscotte/ });
    expect(owner.props.accessibilityState).toMatchObject({ selected: true, disabled: false });
    const trusted = screen.getByRole('radio', { name: /^Personne de confiance, Bientôt/ });
    const educator = screen.getByRole('radio', { name: /^Éducateur, Bientôt/ });
    for (const card of [trusted, educator]) {
      expect(card.props.accessibilityState).toMatchObject({ selected: false, disabled: true });
    }
    expect(screen.getAllByText(t.dogInvitation.soon)).toHaveLength(2);
    expect(screen.getByText(t.dogInvitation.help)).toBeTruthy();
  });

  it('creates the invitation as Owner, then opens the share sheet with the link', async () => {
    const share = jest
      .spyOn(Share, 'share')
      .mockResolvedValue({ action: Share.sharedAction, activityType: undefined });
    const { calls } = await open();

    await fireEvent.press(screen.getByRole('button', { name: t.dogInvitation.share }));

    await waitFor(() => expect(share).toHaveBeenCalledTimes(1));
    expect(share).toHaveBeenCalledWith({
      message: 'Rejoins Biscotte sur Uzu : uzu://invitation/abc123',
    });
    const post = calls.find((call) => call.method === 'POST')!;
    expect(post.path).toBe(`${dogPath}/invitations`);
    expect(post.json).toEqual({ role: 'Owner' });
  });

  it('closes the sheet once the link is shared', async () => {
    jest
      .spyOn(Share, 'share')
      .mockResolvedValue({ action: Share.sharedAction, activityType: undefined });
    await open();
    await fireEvent.press(screen.getByRole('button', { name: t.dogInvitation.share }));
    await waitFor(() => expect(screen).not.toHavePathname(`/dogs/${testDog.id}/invite`));
  });

  it('reuses the link when the share sheet was dismissed: no second invitation', async () => {
    const share = jest
      .spyOn(Share, 'share')
      .mockResolvedValue({ action: Share.dismissedAction, activityType: undefined });
    const { calls } = await open();
    const button = () => screen.getByRole('button', { name: t.dogInvitation.share });

    await fireEvent.press(button());
    await waitFor(() => expect(share).toHaveBeenCalledTimes(1));
    await fireEvent.press(button());
    await waitFor(() => expect(share).toHaveBeenCalledTimes(2));

    expect(calls.filter((call) => call.method === 'POST')).toHaveLength(1);
    expect(screen).toHavePathname(`/dogs/${testDog.id}/invite`);
  });

  it('explains a refusal and does not open the share sheet', async () => {
    const share = jest
      .spyOn(Share, 'share')
      .mockResolvedValue({ action: Share.sharedAction, activityType: undefined });
    await open({ [`POST ${dogPath}/invitations`]: () => problemResponse(403, 'dog.forbidden') });
    await fireEvent.press(screen.getByRole('button', { name: t.dogInvitation.share }));
    expect(await screen.findByText(t.errors['dog.forbidden'])).toBeTruthy();
    expect(share).not.toHaveBeenCalled();
  });
});

describe('leaving a dog, or deleting it', () => {
  let alert: jest.SpyInstance;
  beforeEach(() => {
    alert = jest.spyOn(Alert, 'alert').mockImplementation(() => {});
  });

  const leaveLabel = 'Ne plus m’occuper de Biscotte';

  it('offers « Supprimer » to the last owner, not « Ne plus m’occuper »', async () => {
    mockApi({ [`GET ${dogPath}`]: () => ok(testDog) });
    await signIn();
    await renderRouter(APP_DIR, { initialUrl: `/dogs/${testDog.id}` });
    expect(await screen.findByRole('button', { name: t.common.delete })).toBeTruthy();
    expect(screen.queryByRole('button', { name: leaveLabel })).toBeNull();
  });

  it('offers « Ne plus m’occuper de » instead when other owners remain', async () => {
    mockApi({
      [`GET ${dogPath}`]: () => ok({ ...testDog, members: [member(testUser), member(camille)] }),
    });
    await signIn();
    await renderRouter(APP_DIR, { initialUrl: `/dogs/${testDog.id}` });
    expect(await screen.findByRole('button', { name: leaveLabel })).toBeTruthy();
    expect(screen.queryByRole('button', { name: t.common.delete })).toBeNull();
    expect(screen.getByRole('button', { name: t.common.edit })).toBeTruthy();
  });

  it('confirms with who the dog stays with, then leaves and goes back', async () => {
    const { calls } = mockApi({
      [`GET ${dogPath}`]: () =>
        ok({ ...testDog, members: [member(testUser), member(camille), member(victor)] }),
      [`DELETE ${dogPath}/members/me`]: noContent,
      'GET /api/v1/me/dogs': () => ok([testDog]),
    });
    await signIn();
    await renderRouter(APP_DIR, { initialUrl: '/profile' });
    await fireEvent.press(await screen.findByText('Biscotte'));
    await fireEvent.press(await screen.findByRole('button', { name: leaveLabel }));

    expect(alert).toHaveBeenCalledWith(
      'Ne plus t’occuper de Biscotte ?',
      'Biscotte restera avec Camille et Victor. Tu ne pourras plus le modifier ni l’inscrire.',
      expect.any(Array),
    );
    expect(calls.some((call) => call.method === 'DELETE')).toBe(false);

    await pressAlertButton(alert, t.dog.leaveConfirmAction);

    await waitFor(() => expect(screen).toHavePathname('/profile'));
    expect(
      calls.some((call) => call.method === 'DELETE' && call.path === `${dogPath}/members/me`),
    ).toBe(true);
  });

  it('says so when I turned out to be the last owner (dog.last_owner)', async () => {
    mockApi({
      [`GET ${dogPath}`]: () => ok({ ...testDog, members: [member(testUser), member(camille)] }),
      [`DELETE ${dogPath}/members/me`]: () => problemResponse(409, 'dog.last_owner'),
    });
    await signIn();
    await renderRouter(APP_DIR, { initialUrl: `/dogs/${testDog.id}` });
    await fireEvent.press(await screen.findByRole('button', { name: leaveLabel }));
    await pressAlertButton(alert, t.dog.leaveConfirmAction);
    expect(await screen.findByText(t.errors['dog.last_owner'])).toBeTruthy();
    expect(screen).toHavePathname(`/dogs/${testDog.id}`);
  });

  it('says so when another owner appeared meanwhile (dog.has_other_owners)', async () => {
    mockApi({
      [`GET ${dogPath}`]: () => ok(testDog),
      [`DELETE ${dogPath}`]: () => problemResponse(409, 'dog.has_other_owners'),
    });
    await signIn();
    await renderRouter(APP_DIR, { initialUrl: `/dogs/${testDog.id}` });
    await fireEvent.press(await screen.findByRole('button', { name: t.common.delete }));
    await pressAlertButton(alert, t.common.delete);
    expect(await screen.findByText(t.errors['dog.has_other_owners'])).toBeTruthy();
  });
});

describe('accepting an invitation (uzu://invitation/{token})', () => {
  const token = 'tok-123';
  const previewPath = `/api/v1/dog-invitations/${token}`;
  const sharedDog = { ...testDog, members: [member(camille)] };
  const preview = (overrides: object = {}) => ({
    dog: sharedDog,
    invitedBy: camille,
    role: 'Owner',
    expiresAt: inDays(6),
    ...overrides,
  });

  async function open(routes: Record<string, () => Response>) {
    const result = mockApi(routes);
    await signIn();
    await renderRouter(APP_DIR, { initialUrl: `/invitation/${token}` });
    return result;
  }

  it('shows who invites me, to which dog, with which role and until when', async () => {
    const body = preview();
    await open({ [`GET ${previewPath}`]: () => ok(body) });
    expect(await screen.findByText('Camille t’invite à rejoindre Biscotte')).toBeTruthy();
    expect(screen.getByText('Rôle proposé : Propriétaire')).toBeTruthy();
    expect(screen.getByText(`Valable jusqu’au ${formatDate(body.expiresAt)}`)).toBeTruthy();
    expect(screen.getByLabelText('Humains de Biscotte : Camille')).toBeTruthy();
    expect(screen.getByRole('button', { name: t.dogInvitation.accept })).toBeTruthy();
    expect(screen.getByRole('button', { name: t.dogInvitation.later })).toBeTruthy();
  });

  it('copes with an inviter who deleted their account', async () => {
    await open({ [`GET ${previewPath}`]: () => ok(preview({ invitedBy: null })) });
    expect(await screen.findByText('Tu es invité à rejoindre Biscotte')).toBeTruthy();
  });

  it('accepts, then replaces the screen with the dog’s profile', async () => {
    const accepted = { ...sharedDog, members: [member(camille), member(testUser)] };
    const { calls } = await open({
      [`GET ${previewPath}`]: () => ok(preview()),
      [`POST ${previewPath}/accept`]: () => ok(accepted),
      [`GET ${dogPath}`]: () => ok(accepted),
      [`GET ${dogPath}/invitations`]: () => ok([]),
    });
    await fireEvent.press(await screen.findByRole('button', { name: t.dogInvitation.accept }));

    await waitFor(() => expect(screen).toHavePathname(`/dogs/${testDog.id}`));
    expect(await screen.findByText('avec Camille et toi')).toBeTruthy();
    expect(
      calls.some((call) => call.method === 'POST' && call.path === `${previewPath}/accept`),
    ).toBe(true);
  });

  it('« Plus tard » leaves without accepting', async () => {
    const { calls } = await open({ [`GET ${previewPath}`]: () => ok(preview()) });
    await fireEvent.press(await screen.findByRole('button', { name: t.dogInvitation.later }));
    await waitFor(() => expect(screen).not.toHavePathname(`/invitation/${token}`));
    expect(calls.some((call) => call.method === 'POST')).toBe(false);
  });

  describe('when the link cannot be used', () => {
    it.each([
      [404, 'invitation.not_found', t.dogInvitation.notFoundTitle, t.dogInvitation.notFoundMessage],
      [409, 'invitation.already_used', t.dogInvitation.usedTitle, t.dogInvitation.usedMessage],
      [410, 'invitation.expired', t.dogInvitation.expiredTitle, t.dogInvitation.expiredMessage],
    ])('shows it when reading it answers %s %s', async (status, code, title, message) => {
      await open({ [`GET ${previewPath}`]: () => problemResponse(status, code) });
      expect(await screen.findByText(title)).toBeTruthy();
      expect(screen.getByText(message)).toBeTruthy();
      expect(screen.queryByRole('button', { name: t.dogInvitation.accept })).toBeNull();
    });

    it.each([
      [404, 'invitation.not_found', t.dogInvitation.notFoundTitle],
      [409, 'invitation.already_used', t.dogInvitation.usedTitle],
      [410, 'invitation.expired', t.dogInvitation.expiredTitle],
    ])(
      'shows it when accepting answers %s %s (revoked or used meanwhile)',
      async (status, code, title) => {
        await open({
          [`GET ${previewPath}`]: () => ok(preview()),
          [`POST ${previewPath}/accept`]: () => problemResponse(status, code),
        });
        await fireEvent.press(await screen.findByRole('button', { name: t.dogInvitation.accept }));
        expect(await screen.findByText(title)).toBeTruthy();
      },
    );

    it('says I am already one of the humans, with a button to see the dog (answer of the API)', async () => {
      await open({
        [`GET ${previewPath}`]: () => ok(preview()),
        [`POST ${previewPath}/accept`]: () => problemResponse(409, 'dog.already_owner'),
        [`GET ${dogPath}`]: () => ok(testDog),
        [`GET ${dogPath}/invitations`]: () => ok([]),
      });
      await fireEvent.press(await screen.findByRole('button', { name: t.dogInvitation.accept }));
      expect(await screen.findByText('Tu t’occupes déjà de Biscotte')).toBeTruthy();
      await fireEvent.press(screen.getByRole('button', { name: 'Voir Biscotte' }));
      await waitFor(() => expect(screen).toHavePathname(`/dogs/${testDog.id}`));
    });

    it('says I am already one of the humans before even trying (I am among the members)', async () => {
      await open({
        [`GET ${previewPath}`]: () =>
          ok(preview({ dog: { ...sharedDog, members: [member(camille), member(testUser)] } })),
      });
      expect(await screen.findByText('Tu t’occupes déjà de Biscotte')).toBeTruthy();
      expect(screen.queryByRole('button', { name: t.dogInvitation.accept })).toBeNull();
      expect(screen.getByRole('button', { name: 'Voir Biscotte' })).toBeTruthy();
    });

    it('explains the limit of 20 dogs', async () => {
      await open({
        [`GET ${previewPath}`]: () => ok(preview()),
        [`POST ${previewPath}/accept`]: () => problemResponse(409, 'dog.limit_reached'),
      });
      await fireEvent.press(await screen.findByRole('button', { name: t.dogInvitation.accept }));
      expect(await screen.findByText(t.dogInvitation.limitTitle)).toBeTruthy();
      expect(screen.getByText(t.errors['dog.limit_reached'])).toBeTruthy();
    });

    it('keeps the invitation and shows the error for a failure that may pass (server)', async () => {
      await open({
        [`GET ${previewPath}`]: () => ok(preview()),
        [`POST ${previewPath}/accept`]: () => problemResponse(500),
      });
      await fireEvent.press(await screen.findByRole('button', { name: t.dogInvitation.accept }));
      expect(await screen.findByText(t.errors.server)).toBeTruthy();
      expect(screen.getByRole('button', { name: t.dogInvitation.accept })).toBeTruthy();
    });
  });
});

describe('registering my dog when another owner already did', () => {
  const walk = walkDetail();
  const walkPath = `/api/v1/walks/${walk.id}`;
  const sharedDog = { ...testDog, members: [member(testUser), member(camille)] };

  async function openWalk(detail = walk) {
    const result = mockApi({
      [`GET ${walkPath}`]: () => ok(detail),
      'GET /api/v1/me/dogs': () => ok([sharedDog]),
    });
    await signIn();
    await renderRouter(APP_DIR, { initialUrl: `/walks/${walk.id}` });
    await screen.findByText(walk.title);
    return result;
  }

  it('shows « Déjà inscrit par {prénom} » and does not let me pick the dog', async () => {
    await openWalk(
      walkDetail({
        myDogsRegistrations: [
          { id: 'reg-c', dogId: testDog.id, status: 'Accepted', registeredBy: camille },
        ],
      }),
    );
    const choice = await screen.findByRole('checkbox', {
      name: 'Biscotte, Déjà inscrit par Camille',
    });
    expect(choice.props.accessibilityState).toMatchObject({ disabled: true });
    expect(screen.getByRole('button', { name: t.walk.registerPick })).toBeTruthy();
  });

  it('says « Demande envoyée par {prénom} » for a pending request', async () => {
    await openWalk(
      walkDetail({
        myDogsRegistrations: [
          { id: 'reg-c', dogId: testDog.id, status: 'Pending', registeredBy: camille },
        ],
      }),
    );
    expect(
      await screen.findByRole('checkbox', { name: 'Biscotte, Demande envoyée par Camille' }),
    ).toBeTruthy();
  });

  it('keeps « Déjà inscrit » for a registration I made myself', async () => {
    await openWalk(
      walkDetail({
        myRegistrations: [{ id: 'reg-m', dogId: testDog.id, status: 'Accepted' }],
        myDogsRegistrations: [
          { id: 'reg-m', dogId: testDog.id, status: 'Accepted', registeredBy: testUser },
        ],
      }),
    );
    expect(await screen.findByRole('checkbox', { name: 'Biscotte, Déjà inscrit' })).toBeTruthy();
  });

  it('lets me pick a dog nobody registered', async () => {
    await openWalk();
    const choice = await screen.findByRole('checkbox', { name: 'Biscotte' });
    expect(choice.props.accessibilityState).toMatchObject({ disabled: false });
  });

  it('lists the registration in « Mes inscriptions » with « par {prénom} »', async () => {
    await openWalk(
      walkDetail({
        myDogsRegistrations: [
          { id: 'reg-c', dogId: testDog.id, status: 'Accepted', registeredBy: camille },
        ],
      }),
    );
    expect(await screen.findByText(t.walk.myRegistrations)).toBeTruthy();
    expect(screen.getByText('par Camille')).toBeTruthy();
  });

  it('does not repeat my own registration, nor say « par » for it', async () => {
    await openWalk(
      walkDetail({
        myRegistrations: [{ id: 'reg-m', dogId: testDog.id, status: 'Accepted' }],
        myDogsRegistrations: [
          { id: 'reg-m', dogId: testDog.id, status: 'Accepted', registeredBy: testUser },
        ],
      }),
    );
    expect(await screen.findByText(t.walk.myRegistrations)).toBeTruthy();
    expect(screen.getAllByText(t.walk.cancelRegistration)).toHaveLength(1);
    expect(screen.queryByText(/^par /)).toBeNull();
  });

  it('cancels a registration another owner made, after a confirmation that names them', async () => {
    const alert = jest.spyOn(Alert, 'alert').mockImplementation(() => {});
    const { calls } = mockApi({
      [`GET ${walkPath}`]: () =>
        ok(
          walkDetail({
            myDogsRegistrations: [
              { id: 'reg-c', dogId: testDog.id, status: 'Accepted', registeredBy: camille },
            ],
          }),
        ),
      'GET /api/v1/me/dogs': () => ok([sharedDog]),
      [`DELETE ${walkPath}/registrations/reg-c`]: noContent,
    });
    await signIn();
    await renderRouter(APP_DIR, { initialUrl: `/walks/${walk.id}` });
    await fireEvent.press(
      await screen.findByRole('button', { name: `${t.walk.cancelRegistration} : Biscotte` }),
    );

    expect(alert).toHaveBeenCalledWith(
      'Annuler l’inscription de Biscotte ?',
      'Sa place sera libérée. Camille sera prévenu.',
      expect.any(Array),
    );
    expect(calls.some((call) => call.method === 'DELETE')).toBe(false);
    await pressAlertButton(alert, t.walk.cancelRegistration);
    await waitFor(() =>
      expect(
        calls.some(
          (call) => call.method === 'DELETE' && call.path === `${walkPath}/registrations/reg-c`,
        ),
      ).toBe(true),
    );
  });
});
