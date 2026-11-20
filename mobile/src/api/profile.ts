import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { useEffect, useRef } from 'react';
import { AppState } from 'react-native';

import { useSession } from '@/auth';

import { api, unwrap } from './client';
import { ApiError } from './errors';
import { photoForm, type PickedPhoto } from './photoUpload';
import type { SessionUser } from './types';
import { walksChanged } from './walks';

export const queryKeys = {
  me: ['me'] as const,
  myDogs: ['me', 'dogs'] as const,
  user: (id: string) => ['users', id] as const,
  dog: (id: string) => ['dogs', id] as const,
  dogInvitations: (id: string) => ['dogs', id, 'invitations'] as const,
  dogInvitation: (token: string) => ['dog-invitations', token] as const,
};

const fetchMe = () => unwrap(api.GET('/api/v1/me'));

/** The signed-in user's profile (`GET /me`). */
export function useMe() {
  return useQuery({ queryKey: queryKeys.me, queryFn: fetchMe });
}

const isSuspendedError = (error: unknown) =>
  error instanceof ApiError && error.code === 'account.suspended';

/**
 * Keeps the user stored in the session in step with the server: the session
 * keeps the user returned at sign-in, so a suspension or its lifting (status),
 * made by the team meanwhile, would otherwise only show much later. Reads
 * `GET /me` when the session starts or is restored, when the app returns to
 * the foreground and whenever a request answers `account.suspended`. Failures
 * are ignored (offline: the stored user stays). Mount it once, signed in or not.
 */
export function useSessionUserSync() {
  const queryClient = useQueryClient();
  const { status, user, updateUser } = useSession();
  const signedIn = status === 'signedIn';
  // Read through refs: `updateUser` changes with every session update, which
  // must not restart the effect (and its first sync) each time.
  const latest = useRef({ user, updateUser });
  useEffect(() => {
    latest.current = { user, updateUser };
  });

  useEffect(() => {
    if (!signedIn) return;
    let active = true;
    let running = false;

    async function sync() {
      if (running) return;
      running = true;
      try {
        const fresh = await queryClient.fetchQuery({
          queryKey: queryKeys.me,
          queryFn: fetchMe,
          staleTime: 0,
        });
        const { user: stored, updateUser: update } = latest.current;
        if (active && stored && JSON.stringify(stored) !== JSON.stringify(fresh)) {
          await update(fresh);
        }
      } catch {
        // Offline or server error: keep the stored user.
      } finally {
        running = false;
      }
    }

    void sync();
    const appState = AppState.addEventListener('change', (next) => {
      if (next === 'active') void sync();
    });
    const onEvent = (event: { type: string; action?: { type: string; error?: unknown } }) => {
      if (
        event.type === 'updated' &&
        event.action?.type === 'error' &&
        isSuspendedError(event.action.error)
      ) {
        void sync();
      }
    };
    const unsubscribeQueries = queryClient.getQueryCache().subscribe(onEvent);
    const unsubscribeMutations = queryClient.getMutationCache().subscribe(onEvent);
    return () => {
      active = false;
      appState.remove();
      unsubscribeQueries();
      unsubscribeMutations();
    };
  }, [signedIn, queryClient]);
}

/** Public profile and dogs of any user (`GET /users/{id}`). */
export function usePublicUser(id: string) {
  return useQuery({
    queryKey: queryKeys.user(id),
    queryFn: () => unwrap(api.GET('/api/v1/users/{id}', { params: { path: { id } } })),
  });
}

/** Makes every view of my profile current: cache, session, public profiles. */
function useProfileUpdated() {
  const queryClient = useQueryClient();
  const { updateUser } = useSession();
  return async (user: SessionUser) => {
    queryClient.setQueryData(queryKeys.me, user);
    await updateUser(user);
    void queryClient.invalidateQueries({ queryKey: ['users'] });
    // Walks show my name and photo (organiser, participants, thread authors).
    walksChanged(queryClient);
  };
}

export interface UpdateProfileInput {
  displayName: string;
  /** An empty string clears the bio (API semantics). */
  bio: string;
}

export function useUpdateProfile() {
  const onUpdated = useProfileUpdated();
  return useMutation({
    mutationFn: (body: UpdateProfileInput) => unwrap(api.PATCH('/api/v1/me', { body })),
    onSuccess: onUpdated,
  });
}

export function useUploadMyPhoto() {
  const onUpdated = useProfileUpdated();
  return useMutation({
    mutationFn: (photo: PickedPhoto) =>
      unwrap(api.PUT('/api/v1/me/photo', { body: {}, bodySerializer: () => photoForm(photo) })),
    onSuccess: onUpdated,
  });
}

/**
 * Refreshes everything that shows a dog: my list, its detail, public profiles,
 * and the walks (registered dogs: names and photos, places left).
 * Not awaited, like `walksChanged`.
 */
export function invalidateDogs(queryClient: QueryClient): void {
  void queryClient.invalidateQueries({ queryKey: queryKeys.myDogs });
  void queryClient.invalidateQueries({ queryKey: ['dogs'] });
  void queryClient.invalidateQueries({ queryKey: ['users'] });
  walksChanged(queryClient);
}
