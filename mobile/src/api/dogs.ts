import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { api, unwrap } from './client';
import { photoForm, type PickedPhoto } from './photoUpload';
import { invalidateDogs, queryKeys } from './profile';
import type { CreateDogRequest, DogMemberRole, UpdateDogRequest } from './types';

export function useMyDogs() {
  return useQuery({
    queryKey: queryKeys.myDogs,
    queryFn: () => unwrap(api.GET('/api/v1/me/dogs')),
  });
}

/** Any dog (`GET /dogs/{id}`). */
export function useDog(id: string) {
  return useQuery({
    queryKey: queryKeys.dog(id),
    queryFn: () => unwrap(api.GET('/api/v1/dogs/{id}', { params: { path: { id } } })),
  });
}

export function useCreateDog() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateDogRequest) => unwrap(api.POST('/api/v1/dogs', { body })),
    onSuccess: () => invalidateDogs(queryClient),
  });
}

export function useUpdateDog(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdateDogRequest) =>
      unwrap(api.PATCH('/api/v1/dogs/{id}', { params: { path: { id } }, body })),
    onSuccess: () => invalidateDogs(queryClient),
  });
}

/** Deleting a dog cancels its upcoming registrations (spec §4.1): `invalidateDogs` refreshes the walks too. */
export function useDeleteDog() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => {
      await unwrap(api.DELETE('/api/v1/dogs/{id}', { params: { path: { id } } }));
      return id;
    },
    onSuccess: (id) => {
      queryClient.removeQueries({ queryKey: queryKeys.dog(id) });
      invalidateDogs(queryClient);
    },
  });
}

export function useUploadDogPhoto() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, photo }: { id: string; photo: PickedPhoto }) =>
      unwrap(
        api.PUT('/api/v1/dogs/{id}/photo', {
          params: { path: { id } },
          body: {},
          bodySerializer: () => photoForm(photo),
        }),
      ),
    onSuccess: () => invalidateDogs(queryClient),
  });
}

// ---- Humans of a dog: invitations and leaving (ADR 0042)

/** Pending invitations of a dog, for its owners (no token: it is only shown once, at creation). */
export function useDogInvitations(dogId: string, enabled = true) {
  return useQuery({
    queryKey: queryKeys.dogInvitations(dogId),
    queryFn: () =>
      unwrap(api.GET('/api/v1/dogs/{id}/invitations', { params: { path: { id: dogId } } })),
    enabled,
  });
}

/** Creates an invitation; the response carries the `link` to share (the token is never shown again). */
export function useCreateDogInvitation(dogId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (role: DogMemberRole = 'Owner') =>
      unwrap(
        api.POST('/api/v1/dogs/{id}/invitations', {
          params: { path: { id: dogId } },
          body: { role },
        }),
      ),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.dogInvitations(dogId) });
    },
  });
}

export function useRevokeDogInvitation(dogId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (invitationId: string) =>
      unwrap(
        api.DELETE('/api/v1/dogs/{id}/invitations/{invitationId}', {
          params: { path: { id: dogId, invitationId } },
        }),
      ),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.dogInvitations(dogId) });
    },
  });
}

/** What an invitation offers, read from its link (`uzu://invitation/{token}`) before accepting. */
export function useDogInvitationPreview(token: string) {
  return useQuery({
    queryKey: queryKeys.dogInvitation(token),
    queryFn: () =>
      unwrap(api.GET('/api/v1/dog-invitations/{token}', { params: { path: { token } } })),
  });
}

/** Accepting makes me one of the dog's owners: my dogs, its profile and the walks change. */
export function useAcceptDogInvitation(token: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () =>
      unwrap(api.POST('/api/v1/dog-invitations/{token}/accept', { params: { path: { token } } })),
    onSuccess: (dog) => {
      queryClient.setQueryData(queryKeys.dog(dog.id), dog);
      // The invitation preview is left alone: refetching it now would answer « déjà utilisée »
      // for a moment, before the screen is replaced by the dog's profile.
      invalidateDogs(queryClient);
    },
  });
}

/** I stop being one of the dog's owners (`dog.last_owner` when I am the only one). */
export function useLeaveDog(dogId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () =>
      unwrap(api.DELETE('/api/v1/dogs/{id}/members/me', { params: { path: { id: dogId } } })),
    onSuccess: () => invalidateDogs(queryClient),
  });
}
