import { act, fireEvent, renderRouter, screen, waitFor, within } from 'expo-router/testing-library';
import { Alert, ScrollView } from 'react-native';

import type { DogMember } from '@/api/types';

import { sessionStore } from '@/auth/sessionStore';
import { pickPhoto } from '@/lib/photoPicker';
import { t } from '@/strings';
import { authResponse, testDog, testUser } from '@/test/fixtures';
import { mockApi, ok, problemResponse, screenCalls } from '@/test/mockApi';

jest.mock('@/lib/photoPicker', () => ({
  pickPhoto: jest.fn(),
  CameraPermissionError: class CameraPermissionError extends Error {},
}));

// Native date picker replaced by a button that picks 5 March 2021.
jest.mock('@react-native-community/datetimepicker', () => {
  const React = jest.requireActual<typeof import('react')>('react');
  const { Pressable: P, Text: T } =
    jest.requireActual<typeof import('react-native')>('react-native');
  return {
    __esModule: true,
    default: ({ onChange }: { onChange: (e: unknown, d: Date) => void }) =>
      React.createElement(
        P,
        {
          accessibilityRole: 'button',
          onPress: () => onChange({ type: 'set' }, new Date(2021, 2, 5)),
        },
        React.createElement(T, null, 'Date du test'),
      ),
    DateTimePickerAndroid: { open: jest.fn() },
  };
});

const APP_DIR = './src/app';
const dogPath = `/api/v1/dogs/${testDog.id}`;
const camille = {
  id: '6f1d3c1e-0000-4000-8000-0000000000c1',
  displayName: 'Camille',
  photoUrl: null,
};
const victor = {
  id: '6f1d3c1e-0000-4000-8000-0000000000c2',
  displayName: 'Victor',
  photoUrl: null,
};
const member = (user: DogMember['user']): DogMember => ({
  user: { id: user.id, displayName: user.displayName, photoUrl: user.photoUrl },
  role: 'Owner',
});

afterEach(async () => {
  await act(() => sessionStore.clear());
});

async function signIn() {
  await sessionStore.set(authResponse());
}

describe('dog detail', () => {
  it('shows every field in French with the age, and edit/delete for my dog', async () => {
    mockApi({ [`GET ${dogPath}`]: () => ok({ ...testDog, birthDate: '2000-01-01' }) });
    await signIn();
    await renderRouter(APP_DIR, { initialUrl: `/dogs/${testDog.id}` });

    expect(await screen.findByText('Biscotte')).toBeTruthy();
    expect(screen.getByText('Border collie')).toBeTruthy();
    expect(screen.getByText('Moyen')).toBeTruthy();
    expect(screen.getByText('À l’aise')).toBeTruthy();
    expect(screen.getByText('Légère')).toBeTruthy();
    expect(screen.getByText('En longe')).toBeTruthy();
    expect(screen.getByText(/^\d+ ans$/)).toBeTruthy();
    expect(screen.getByText('Adore les balades en forêt.')).toBeTruthy();
    expect(screen.getByRole('button', { name: t.common.edit })).toBeTruthy();
    expect(screen.getByRole('button', { name: t.common.delete })).toBeTruthy();
  });

  it('is read-only for someone else’s dog: report, no edit or delete', async () => {
    mockApi({
      [`GET ${dogPath}`]: () =>
        ok({ ...testDog, members: [member(camille)], birthDate: null, breed: null }),
    });
    await signIn();
    await renderRouter(APP_DIR, { initialUrl: `/dogs/${testDog.id}` });

    expect(await screen.findByText(t.dog.noBreed)).toBeTruthy();
    expect(screen.getByText(t.dog.noBirthDate)).toBeTruthy();
    expect(screen.queryByRole('button', { name: t.common.edit })).toBeNull();
    expect(screen.queryByRole('button', { name: t.common.delete })).toBeNull();
    expect(screen.getByRole('button', { name: t.report.action })).toBeTruthy();
    // The owner is reached through « Ses humains », not through a button of its own.
    expect(screen.queryByText('Voir le propriétaire')).toBeNull();
  });

  it('sets the sections in order: character, description, humans', async () => {
    mockApi({ [`GET ${dogPath}`]: () => ok(testDog) });
    await signIn();
    await renderRouter(APP_DIR, { initialUrl: `/dogs/${testDog.id}` });
    await screen.findByText('Biscotte');

    const sections: string[] = [t.dog.characterTitle, t.dog.descriptionTitle, t.dog.humansTitle];
    const titles = screen
      .getAllByRole('header')
      .map((node) => node.props.children as string)
      .filter((title) => sections.includes(title));
    expect(titles).toEqual(sections);
    // The five characteristics are all there, the leash habit included.
    expect(screen.getByLabelText(`${t.dog.size} : Moyen`)).toBeTruthy();
    expect(screen.getByLabelText(`${t.dog.leashHabit} : En longe`)).toBeTruthy();
  });

  describe('the humans', () => {
    const open = async (members: DogMember[]) => {
      mockApi({ [`GET ${dogPath}`]: () => ok({ ...testDog, members }) });
      await signIn();
      await renderRouter(APP_DIR, { initialUrl: `/dogs/${testDog.id}` });
      await screen.findByText('Biscotte');
    };

    it('says « avec toi » when I am the only one, and lists me as « Toi » without a link', async () => {
      await open([member(testUser)]);
      expect(screen.getByText('avec toi')).toBeTruthy();
      expect(screen.getByLabelText('Toi, Propriétaire')).toBeTruthy();
      expect(screen.queryByRole('button', { name: 'Toi, Propriétaire' })).toBeNull();
    });

    it('puts me last, called « toi »', async () => {
      await open([member(testUser), member(camille)]);
      expect(screen.getByText('avec Camille et toi')).toBeTruthy();
    });

    it('names two other humans', async () => {
      await open([member(camille), member(victor)]);
      expect(screen.getByText('avec Camille et Victor')).toBeTruthy();
      expect(screen.getByLabelText('Humains de Biscotte : Camille et Victor')).toBeTruthy();
    });

    it('names three humans', async () => {
      await open([member(camille), member(victor), member(testUser)]);
      expect(screen.getByText('avec Camille, Victor et toi')).toBeTruthy();
    });

    it('counts the rest past three, and keeps three pictures and a « +N » pill', async () => {
      const crowd = ['Ana', 'Ben', 'Cléo', 'Dan', 'Eva'].map((name, index) =>
        member({ id: `human-${index}`, displayName: name, photoUrl: null }),
      );
      await open(crowd);
      expect(screen.getByText('avec Ana, Ben et 3 autres')).toBeTruthy();
      expect(screen.getByText('+2')).toBeTruthy();
      const stack = within(screen.getByTestId('dog-humans-stack'));
      expect(stack.getAllByLabelText(/^Photo d(e |’)/, { hidden: true })).toHaveLength(3);
    });

    it('lists one row per human with the role, and opens a profile except mine', async () => {
      await open([member(testUser), member(camille)]);
      expect(screen.getAllByText('Propriétaire')).toHaveLength(2);

      await fireEvent.press(screen.getByRole('button', { name: 'Camille, Propriétaire' }));
      expect(screen).toHavePathname(`/users/${camille.id}`);
    });

    it('does not open my own row', async () => {
      await open([member(testUser)]);
      await fireEvent.press(screen.getByLabelText('Toi, Propriétaire'));
      expect(screen).toHavePathname(`/dogs/${testDog.id}`);
    });

    it('scrolls to « Ses humains » when the line is touched', async () => {
      await open([member(camille), member(victor)]);
      const scrollTo = jest.spyOn(ScrollView.prototype, 'scrollTo');
      await fireEvent.press(screen.getByText('avec Camille et Victor'));
      expect(scrollTo).toHaveBeenCalledWith(expect.objectContaining({ animated: true }));
      await fireEvent.press(screen.getByLabelText('Humains de Biscotte : Camille et Victor'));
      expect(scrollTo).toHaveBeenCalledTimes(2);
      scrollTo.mockRestore();
    });
  });

  describe('mine is computed from the members', () => {
    it('offers edit, and « Ne plus m’occuper de » instead of delete, when I am one of several owners', async () => {
      mockApi({
        [`GET ${dogPath}`]: () => ok({ ...testDog, members: [member(camille), member(testUser)] }),
      });
      await signIn();
      await renderRouter(APP_DIR, { initialUrl: `/dogs/${testDog.id}` });
      expect(await screen.findByRole('button', { name: t.common.edit })).toBeTruthy();
      expect(screen.getByRole('button', { name: 'Ne plus m’occuper de Biscotte' })).toBeTruthy();
      expect(screen.queryByRole('button', { name: t.common.delete })).toBeNull();
      expect(screen.queryByRole('button', { name: t.report.action })).toBeNull();
    });

    it('offers only to report when I am not a member', async () => {
      mockApi({
        [`GET ${dogPath}`]: () => ok({ ...testDog, members: [member(camille), member(victor)] }),
      });
      await signIn();
      await renderRouter(APP_DIR, { initialUrl: `/dogs/${testDog.id}` });
      expect(await screen.findByRole('button', { name: t.report.action })).toBeTruthy();
      expect(screen.queryByRole('button', { name: t.common.edit })).toBeNull();
    });
  });

  it('shows a French error for an unknown dog', async () => {
    mockApi({ [`GET ${dogPath}`]: () => problemResponse(404, 'dog.not_found') });
    await signIn();
    await renderRouter(APP_DIR, { initialUrl: `/dogs/${testDog.id}` });
    expect(await screen.findByText(t.errors['dog.not_found'])).toBeTruthy();
    expect(screen.getByText(t.dog.notFoundTitle)).toBeTruthy();
    expect(screen.queryByText(t.states.errorTitle)).toBeNull();
  });
});

describe('the limit of 20 dogs', () => {
  const dogsOf = (count: number) =>
    Array.from({ length: count }, (_, index) => ({
      ...testDog,
      id: `dog-${index}`,
      name: `Chien ${index}`,
    }));
  const routes = (count: number) => ({
    'GET /api/v1/me': () => ok(testUser),
    'GET /api/v1/me/dogs': () => ok(dogsOf(count)),
    'GET /api/v1/me/organizations': () => ok([]),
  });

  it('still offers to add a dog with 19', async () => {
    mockApi(routes(19));
    await signIn();
    await renderRouter(APP_DIR, { initialUrl: '/profile' });
    expect(await screen.findByText('Chien 18')).toBeTruthy();
    expect(screen.getByRole('button', { name: t.dog.add })).toBeTruthy();
    expect(screen.queryByText(t.errors['dog.limit_reached'])).toBeNull();
  });

  it('replaces the « add » button by an explanation at 20', async () => {
    mockApi(routes(20));
    await signIn();
    await renderRouter(APP_DIR, { initialUrl: '/profile' });
    expect(await screen.findByText('Chien 19')).toBeTruthy();
    expect(screen.queryByRole('button', { name: t.dog.add })).toBeNull();
    expect(screen.getByText(t.errors['dog.limit_reached'])).toBeTruthy();
  });

  it('shows no form on the new-dog screen at 20, with the reason', async () => {
    mockApi(routes(20));
    await signIn();
    await renderRouter(APP_DIR, { initialUrl: '/dogs/new' });
    expect(await screen.findByText(t.dog.limitTitle)).toBeTruthy();
    expect(screen.getByText(t.errors['dog.limit_reached'])).toBeTruthy();
    expect(screen.queryByLabelText(t.dog.name)).toBeNull();
  });

  it('opens the form below the limit', async () => {
    mockApi(routes(5));
    await signIn();
    await renderRouter(APP_DIR, { initialUrl: '/dogs/new' });
    expect(await screen.findByLabelText(t.dog.name)).toBeTruthy();
  });
});

describe('delete a dog', () => {
  let alert: jest.SpyInstance;
  beforeEach(() => {
    alert = jest.spyOn(Alert, 'alert').mockImplementation(() => {});
  });
  afterEach(() => alert.mockRestore());

  /** Presses the destructive button of the last confirmation. */
  async function confirm() {
    const buttons = alert.mock.calls.at(-1)![2] as { text: string; onPress?: () => void }[];
    await act(async () => buttons.find((b) => b.text === t.common.delete)!.onPress!());
  }

  it('confirms with a message about the cancelled registrations, then deletes', async () => {
    let dogs = [testDog];
    const { calls } = mockApi({
      [`GET ${dogPath}`]: () => ok(testDog),
      [`DELETE ${dogPath}`]: () => {
        dogs = [];
        return new Response(null, { status: 204 });
      },
      'GET /api/v1/me': () => ok(testUser),
      'GET /api/v1/me/dogs': () => ok(dogs),
    });
    await signIn();
    await renderRouter(APP_DIR, { initialUrl: '/profile' });
    await fireEvent.press(await screen.findByText('Biscotte'));
    await fireEvent.press(await screen.findByRole('button', { name: t.common.delete }));

    expect(alert).toHaveBeenCalledWith(
      'Supprimer Biscotte ?',
      expect.stringContaining('inscriptions à venir seront annulées'),
      expect.any(Array),
    );
    expect(calls.some((c) => c.method === 'DELETE')).toBe(false);

    await confirm();

    await waitFor(() => expect(screen).toHavePathname('/profile'));
    expect(await screen.findByText(t.dog.myDogsEmptyTitle)).toBeTruthy();
    expect(calls.some((c) => c.method === 'DELETE' && c.path === dogPath)).toBe(true);
  });

  it('keeps the dog and shows the error when deletion fails', async () => {
    mockApi({
      [`GET ${dogPath}`]: () => ok(testDog),
      [`DELETE ${dogPath}`]: () => problemResponse(403, 'dog.forbidden'),
    });
    await signIn();
    await renderRouter(APP_DIR, { initialUrl: `/dogs/${testDog.id}` });
    await fireEvent.press(await screen.findByRole('button', { name: t.common.delete }));
    await confirm();
    expect(await screen.findByText(t.errors['dog.forbidden'])).toBeTruthy();
  });
});

describe('dog form', () => {
  const created = { ...testDog, id: 'new-dog-id', name: 'Nala', photoUrl: null };

  async function fillValid() {
    await fireEvent.changeText(screen.getByLabelText(t.dog.name), ' Nala ');
    await fireEvent.press(screen.getByRole('radio', { name: 'Petit' }));
    await fireEvent.press(screen.getByRole('radio', { name: 'Sélectif' }));
    await fireEvent.press(screen.getByRole('radio', { name: 'Aucune' }));
    await fireEvent.press(screen.getByRole('radio', { name: 'En laisse' }));
  }

  it('validates required fields before sending anything', async () => {
    const { calls } = mockApi({});
    await signIn();
    await renderRouter(APP_DIR, { initialUrl: '/dogs/new' });

    await fireEvent.press(await screen.findByRole('button', { name: t.dog.add }));

    expect(screen.getAllByText(t.common.required)).toHaveLength(5);
    // Nothing is sent (the screen only reads the list of dogs, for the limit of 20).
    expect(calls.filter((c) => c.method !== 'GET')).toHaveLength(0);
  });

  it('creates the dog, then uploads its photo, then opens it', async () => {
    jest.mocked(pickPhoto).mockResolvedValue({
      uri: 'file:///tmp/nala.jpg',
      name: 'photo.jpg',
      type: 'image/jpeg',
    });
    const { calls } = mockApi({
      'POST /api/v1/dogs': () => ok(created),
      'PUT /api/v1/dogs/new-dog-id/photo': () => ok({ ...created, photoUrl: '/photos/n.jpg' }),
      'GET /api/v1/dogs/new-dog-id': () => ok(created),
    });
    await signIn();
    await renderRouter(APP_DIR, { initialUrl: '/dogs/new' });
    await screen.findByLabelText(t.dog.name);

    await fillValid();
    await fireEvent.changeText(screen.getByLabelText(t.dog.breed), 'Husky');
    await fireEvent.press(screen.getByRole('button', { name: t.photo.fromLibrary }));
    await fireEvent.press(screen.getByRole('button', { name: new RegExp(t.dog.birthDatePick) }));
    await fireEvent.press(await screen.findByText('Date du test'));
    await fireEvent.press(screen.getByRole('button', { name: t.dog.add }));

    expect(await screen.findByText('Nala')).toBeTruthy();
    expect(screen).toHavePathname('/dogs/new-dog-id');
    const sent = screenCalls(calls).filter(
      (c) => c.method !== 'GET' || c.path !== '/api/v1/me/dogs',
    );
    expect(sent.map((c) => `${c.method} ${c.path}`).slice(0, 2)).toEqual([
      'POST /api/v1/dogs',
      'PUT /api/v1/dogs/new-dog-id/photo',
    ]);
    expect((sent[1]!.form?.get('file') as File).name).toBe('photo.jpg');
    expect(sent[0]!.json).toEqual({
      name: 'Nala',
      size: 'Small',
      sociability: 'Selective',
      reactivity: 'None',
      leashHabit: 'OnLeash',
      birthDate: '2021-03-05',
      breed: 'Husky',
    });
  });

  it('shows server validation errors on the right field', async () => {
    mockApi({
      'POST /api/v1/dogs': () =>
        problemResponse(400, 'validation.failed', { errors: { name: ['bad'] } }),
    });
    await signIn();
    await renderRouter(APP_DIR, { initialUrl: '/dogs/new' });
    await screen.findByLabelText(t.dog.name);
    await fillValid();
    await fireEvent.press(screen.getByRole('button', { name: t.dog.add }));

    expect(await screen.findByText(t.common.fieldInvalid)).toBeTruthy();
    expect(screen.getByText(t.errors['validation.failed'])).toBeTruthy();
    expect(screen).toHavePathname('/dogs/new');
  });

  it('edits my dog: sends every field and refreshes the profile list', async () => {
    let current = testDog;
    const { calls } = mockApi({
      [`GET ${dogPath}`]: () => ok(current),
      [`PATCH ${dogPath}`]: (req) => {
        current = { ...current, ...(req.json as object) } as typeof testDog;
        return ok(current);
      },
      'GET /api/v1/me': () => ok(testUser),
      'GET /api/v1/me/dogs': () => ok([current]),
    });
    await signIn();
    await renderRouter(APP_DIR, { initialUrl: '/profile' });
    await fireEvent.press(await screen.findByText('Biscotte'));
    await fireEvent.press(await screen.findByRole('button', { name: t.common.edit }));

    const name = await screen.findByDisplayValue('Biscotte');
    await fireEvent.changeText(name, 'Biscotte II');
    await fireEvent.changeText(screen.getByLabelText(t.dog.breed), '');
    await fireEvent.press(screen.getByRole('radio', { name: 'Grand' }));
    await fireEvent.press(screen.getByRole('button', { name: t.common.save }));

    expect(await screen.findByText('Biscotte II')).toBeTruthy();
    expect(calls.find((c) => c.method === 'PATCH')!.json).toMatchObject({
      name: 'Biscotte II',
      size: 'Large',
      breed: null,
      birthDate: '2022-03-15',
      description: 'Adore les balades en forêt.',
    });
  });

  it('shows dog.size_in_use on the size field, with its own message, not « Valeur invalide »', async () => {
    const { calls } = mockApi({
      [`GET ${dogPath}`]: () => ok(testDog),
      [`PATCH ${dogPath}`]: () => problemResponse(409, 'dog.size_in_use'),
    });
    await signIn();
    await renderRouter(APP_DIR, { initialUrl: `/dogs/${testDog.id}/edit` });
    await screen.findByDisplayValue('Biscotte');
    await fireEvent.press(screen.getByRole('radio', { name: 'Grand' }));
    await fireEvent.press(screen.getByRole('button', { name: t.common.save }));

    // On the field, once: the form shows no second copy under the button.
    expect(await screen.findAllByText(t.errors['dog.size_in_use'])).toHaveLength(1);
    expect(screen.queryByText(t.common.fieldInvalid)).toBeNull();
    expect(screen.queryByText(t.errors['validation.failed'])).toBeNull();
    expect(screen).toHavePathname(`/dogs/${testDog.id}/edit`);
    // The dog is read again: the screens behind show what the server now knows.
    await waitFor(() =>
      expect(calls.filter((c) => c.method === 'GET' && c.path === dogPath).length).toBeGreaterThan(
        1,
      ),
    );
  });

  it('says clearly that 20 dogs is the maximum when the server refuses another one', async () => {
    mockApi({ 'POST /api/v1/dogs': () => problemResponse(409, 'dog.limit_reached') });
    await signIn();
    await renderRouter(APP_DIR, { initialUrl: '/dogs/new' });
    await screen.findByLabelText(t.dog.name);
    await fillValid();
    await fireEvent.press(screen.getByRole('button', { name: t.dog.add }));
    expect(await screen.findByText(t.errors['dog.limit_reached'])).toBeTruthy();
  });

  it('shows a paw, not the initial of « Chien », while the new dog has no name (defect D3)', async () => {
    mockApi({});
    await signIn();
    await renderRouter(APP_DIR, { initialUrl: '/dogs/new' });
    expect(await screen.findByLabelText(t.a11y.photo)).toBeTruthy();
    expect(screen.queryByText('C')).toBeNull();
    await fireEvent.changeText(screen.getByLabelText(t.dog.name), 'Nala');
    expect(screen.getByText('N')).toBeTruthy();
  });

  it('leaves no dead end when the dog is saved but its photo is not', async () => {
    const alert = jest.spyOn(Alert, 'alert').mockImplementation(() => {});
    jest.mocked(pickPhoto).mockResolvedValue({
      uri: 'file:///tmp/new.jpg',
      name: 'photo.jpg',
      type: 'image/jpeg',
    });
    let uploads = 0;
    mockApi({
      [`GET ${dogPath}`]: () => ok(testDog),
      [`PATCH ${dogPath}`]: () => ok(testDog),
      [`PUT ${dogPath}/photo`]: () => {
        uploads += 1;
        return uploads === 1 ? problemResponse(413, 'photo.too_large') : ok(testDog);
      },
    });
    await signIn();
    await renderRouter(APP_DIR, { initialUrl: `/dogs/${testDog.id}/edit` });
    await screen.findByDisplayValue('Biscotte');
    await fireEvent.press(screen.getByRole('button', { name: t.photo.fromLibrary }));
    await fireEvent.press(screen.getByRole('button', { name: t.common.save }));

    await waitFor(() => expect(alert).toHaveBeenCalled());
    const [title, message, buttons] = alert.mock.calls[0]!;
    expect(title).toBe(t.dog.updatedPhotoFailed);
    expect(message).toBe(t.errors['photo.too_large']);
    expect(buttons?.map((b) => b.text)).toEqual([t.dog.photoLater, t.dog.photoRetry]);
    await act(async () => buttons![1]!.onPress!());
    await waitFor(() => expect(uploads).toBe(2));
    alert.mockRestore();
  });

  it('refuses to edit a dog that is not mine', async () => {
    mockApi({ [`GET ${dogPath}`]: () => ok({ ...testDog, members: [member(camille)] }) });
    await signIn();
    await renderRouter(APP_DIR, { initialUrl: `/dogs/${testDog.id}/edit` });
    expect(await screen.findByText(t.errors['dog.forbidden'])).toBeTruthy();
    expect(screen.queryByLabelText(t.dog.name)).toBeNull();
  });
});
