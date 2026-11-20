import { Stack, useLocalSearchParams } from 'expo-router';
import { useEffect, useState } from 'react';

import { useMyDogs } from '@/api/dogs';
import { errorMessage } from '@/api/errors';
import type { PickedPhoto } from '@/api/photoUpload';
import { useUpdateProfile, useUploadMyPhoto } from '@/api/profile';
import { useSession } from '@/auth';
import { Button, PhotoPicker, Screen, Text, TextField, useScrollToFirstError } from '@/components';
import {
  limits,
  profileFields,
  serverFieldErrors,
  validateProfile,
  type FieldErrors,
  type ProfileFormValues,
} from '@/lib/forms';
import { useFocusedRouter } from '@/lib/hooks';
import { takePendingInvitation } from '@/lib/pendingInvitation';
import { endWelcome } from '@/lib/welcomeNudge';
import { t } from '@/strings';

/** Edit my profile; `?welcome=1` adds the first-run sentence (new member). */
export default function EditProfileScreen() {
  const { welcome } = useLocalSearchParams<{ welcome?: string }>();
  return (
    <Screen>
      <Stack.Screen options={{ title: t.profile.editTitle }} />
      <EditProfileForm welcome={Boolean(welcome)} />
    </Screen>
  );
}

/** Rendered inside `Screen`, so a failed save can scroll to the first field in error. */
function EditProfileForm({ welcome }: { welcome: boolean }) {
  const { user } = useSession();
  const scrollToFirstError = useScrollToFirstError();
  const router = useFocusedRouter();
  const update = useUpdateProfile();
  const dogs = useMyDogs();
  const uploadPhoto = useUploadMyPhoto();
  const [values, setValues] = useState<ProfileFormValues>({
    // The API default name is a placeholder: start from an empty field instead.
    displayName: welcome ? '' : (user?.displayName ?? ''),
    bio: user?.bio ?? '',
  });
  const [photo, setPhoto] = useState<PickedPhoto | null>(null);
  const [errors, setErrors] = useState<FieldErrors<keyof ProfileFormValues>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [photoError, setPhotoError] = useState<string | undefined>();
  const saving = update.isPending || uploadPhoto.isPending;

  // However the welcome is left, whatever was waiting for it (an invitation link) may go on.
  useEffect(() => (welcome ? endWelcome : undefined), [welcome]);

  /**
   * An invitation link that came before the account existed goes first: accepting
   * it gives this new member a dog, so « Ajoute ton premier chien » would be
   * asked in vain. Returns false when no link is waiting.
   */
  function goToPendingInvitation(): boolean {
    const token = takePendingInvitation();
    if (!token) return false;
    router.replace({ pathname: '/invitation/[token]', params: { token } });
    return true;
  }

  async function save() {
    const found = validateProfile(values);
    setErrors(found);
    setFormError(null);
    setPhotoError(undefined);
    if (Object.keys(found).length > 0) {
      void scrollToFirstError(found);
      return;
    }

    try {
      await update.mutateAsync({ displayName: values.displayName.trim(), bio: values.bio.trim() });
    } catch (error) {
      const serverErrors = serverFieldErrors(error, profileFields);
      setErrors(serverErrors);
      setFormError(errorMessage(error));
      void scrollToFirstError(serverErrors);
      return;
    }
    if (photo) {
      try {
        await uploadPhoto.mutateAsync(photo);
      } catch (error) {
        setPhotoError(errorMessage(error));
        setFormError(t.profile.photoSaveFailed);
        return;
      }
    }
    // A new member cannot register to a walk without a dog: carry on to adding one.
    if (welcome) {
      if (goToPendingInvitation()) return;
      const mine = dogs.data ?? (await dogs.refetch()).data;
      if (mine?.length === 0) {
        router.replace({ pathname: '/dogs/new', params: { welcome: '1' } });
        return;
      }
    }
    router.back();
  }

  const name = values.displayName || user?.displayName || '';
  return (
    <>
      {welcome ? <Text color="textMuted">{t.profile.welcome}</Text> : null}
      <PhotoPicker
        name={name}
        photoUrl={user?.photoUrl}
        picked={photo}
        onPick={(picked) => {
          setPhoto(picked);
          setPhotoError(undefined);
        }}
        error={photoError}
        disabled={saving}
      />
      <TextField
        name="displayName"
        label={t.profile.displayName}
        value={values.displayName}
        onChangeText={(text) => {
          setValues((current) => ({ ...current, displayName: text }));
          setErrors((current) => ({ ...current, displayName: undefined }));
        }}
        error={errors.displayName}
        maxLength={limits.displayName}
        autoCapitalize="words"
      />
      <TextField
        name="bio"
        label={t.profile.bio}
        value={values.bio}
        onChangeText={(text) => {
          setValues((current) => ({ ...current, bio: text }));
          setErrors((current) => ({ ...current, bio: undefined }));
        }}
        error={errors.bio}
        hint={t.profile.bioHint}
        maxLength={limits.bio}
        multiline
      />
      {formError ? (
        <Text color="danger" accessibilityLiveRegion="polite">
          {formError}
        </Text>
      ) : null}
      <Button label={t.common.save} loading={saving} onPress={save} />
      {welcome ? (
        <Button
          label={t.profile.later}
          variant="secondary"
          onPress={() => {
            if (!goToPendingInvitation()) router.back();
          }}
        />
      ) : null}
    </>
  );
}
