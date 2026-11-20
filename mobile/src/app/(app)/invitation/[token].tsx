import { Stack, useLocalSearchParams } from 'expo-router';
import { useState, type ReactNode } from 'react';
import { View } from 'react-native';

import { useAcceptDogInvitation, useDogInvitationPreview } from '@/api/dogs';
import { ApiError, errorMessage } from '@/api/errors';
import { useSession } from '@/auth';
import {
  Avatar,
  AvatarStack,
  Badge,
  Button,
  EmptyState,
  ErrorState,
  leaveScreen,
  LoadingState,
  Screen,
  Text,
  type StateAction,
} from '@/components';
import { formatDate } from '@/lib/dates';
import { isDogOwner, listNames } from '@/lib/dogMembers';
import { useFocusedRouter } from '@/lib/hooks';
import { enumLabel, format, t } from '@/strings';
import { useTheme } from '@/theme';

const DOG_AVATAR = 96;

interface Problem {
  title: string;
  message: string;
  action?: StateAction;
}

/**
 * The invitation cannot be accepted for a reason the person can understand:
 * unknown, already used, expired (the three rules of the invitation itself),
 * already one of the humans, or at the limit of dogs. `null` for any other
 * failure (network, server): those keep the usual retry.
 */
function problemFor(
  error: unknown,
  dog: { id: string; name: string } | undefined,
  seeDog: (id: string) => void,
): Problem | null {
  if (!(error instanceof ApiError)) return null;
  switch (error.code) {
    case 'invitation.not_found':
      return {
        title: t.dogInvitation.notFoundTitle,
        message: t.dogInvitation.notFoundMessage,
        action: { label: t.common.back, onPress: leaveScreen },
      };
    case 'invitation.already_used':
      return {
        title: t.dogInvitation.usedTitle,
        message: t.dogInvitation.usedMessage,
        action: { label: t.common.back, onPress: leaveScreen },
      };
    case 'invitation.expired':
      return {
        title: t.dogInvitation.expiredTitle,
        message: t.dogInvitation.expiredMessage,
        action: { label: t.common.back, onPress: leaveScreen },
      };
    case 'dog.already_owner':
      return alreadyOwner(dog, seeDog);
    case 'dog.limit_reached':
      return {
        title: t.dogInvitation.limitTitle,
        message: t.errors['dog.limit_reached'],
        action: { label: t.common.back, onPress: leaveScreen },
      };
    default:
      return null;
  }
}

function alreadyOwner(
  dog: { id: string; name: string } | undefined,
  seeDog: (id: string) => void,
): Problem {
  return {
    title: dog
      ? format(t.dogInvitation.alreadyOwnerTitle, { name: dog.name })
      : t.errors['dog.already_owner'],
    message: dog
      ? format(t.dogInvitation.alreadyOwnerMessage, { name: dog.name })
      : t.errors['dog.already_owner'],
    action: dog
      ? { label: format(t.dogInvitation.seeDog, { name: dog.name }), onPress: () => seeDog(dog.id) }
      : { label: t.common.back, onPress: leaveScreen },
  };
}

/**
 * Landing screen of the link `uzu://invitation/{token}` (ADR 0042): who invites
 * me to which dog, with which role, until when. « Accepter » makes me one of its
 * owners and replaces this screen with the dog's profile.
 */
export default function DogInvitationScreen() {
  const { token } = useLocalSearchParams<{ token: string }>();
  const { user } = useSession();
  const { spacing } = useTheme();
  const router = useFocusedRouter();
  const preview = useDogInvitationPreview(token);
  const accept = useAcceptDogInvitation(token);
  const [acceptError, setAcceptError] = useState<unknown>(null);

  const seeDog = (dogId: string) =>
    router.replace({ pathname: '/dogs/[id]', params: { id: dogId } });

  /** `centered`: a state that fills the screen, not the scrolling card. */
  const { content, centered } = ((): { content: ReactNode; centered: boolean } => {
    if (preview.isPending) return { content: <LoadingState />, centered: true };
    if (preview.isError) {
      const problem = problemFor(preview.error, undefined, seeDog);
      return {
        content: problem ? (
          <EmptyState icon="warning" {...problem} />
        ) : (
          <ErrorState error={preview.error} onRetry={() => preview.refetch()} />
        ),
        centered: true,
      };
    }

    const { dog, invitedBy, role, expiresAt } = preview.data;
    const problem =
      (isDogOwner(dog, user?.id) ? alreadyOwner(dog, seeDog) : null) ??
      problemFor(acceptError, dog, seeDog);
    if (problem) return { content: <EmptyState icon="warning" {...problem} />, centered: true };

    const users = dog.members.map((member) => member.user);
    const card = (
      <View style={{ gap: spacing.lg }}>
        <View style={{ alignItems: 'center', gap: spacing.sm }}>
          <View style={{ width: DOG_AVATAR, height: DOG_AVATAR }}>
            <Avatar
              name={dog.name}
              photoUrl={dog.photoUrl}
              size={DOG_AVATAR}
              placeholderIcon="paw"
            />
            {users.length > 0 ? (
              <View style={{ position: 'absolute', right: -spacing.sm, bottom: -spacing.xs }}>
                <AvatarStack
                  users={users}
                  accessibilityLabel={format(t.dogInvitation.humansOf, {
                    name: dog.name,
                    names: listNames(
                      users.map((human) => human.displayName),
                      users.length,
                    ),
                  })}
                />
              </View>
            ) : null}
          </View>
          <Text variant="title" align="center">
            {invitedBy
              ? format(t.dogInvitation.invitedBy, {
                  inviter: invitedBy.displayName,
                  name: dog.name,
                })
              : format(t.dogInvitation.invitedByUnknown, { name: dog.name })}
          </Text>
          {dog.breed ? (
            <Text color="textMuted" align="center">
              {dog.breed}
            </Text>
          ) : null}
        </View>
        <View style={{ alignItems: 'center', gap: spacing.xs }}>
          <Badge
            tone="primary"
            label={format(t.dogInvitation.roleOffered, { role: enumLabel('DogMemberRole', role) })}
          />
          <Text variant="caption" color="textMuted" align="center">
            {format(t.dogInvitation.expiresOn, { date: formatDate(expiresAt) })}
          </Text>
        </View>
        <View style={{ gap: spacing.md }}>
          <Button
            label={t.dogInvitation.accept}
            loading={accept.isPending}
            onPress={() => {
              setAcceptError(null);
              accept.mutate(undefined, {
                onSuccess: (accepted) => seeDog(accepted.id),
                onError: setAcceptError,
              });
            }}
          />
          <Button label={t.dogInvitation.later} variant="secondary" onPress={leaveScreen} />
          {acceptError && !problemFor(acceptError, dog, seeDog) ? (
            <Text color="danger" accessibilityLiveRegion="polite">
              {errorMessage(acceptError)}
            </Text>
          ) : null}
        </View>
      </View>
    );
    return { content: card, centered: false };
  })();

  return (
    <Screen scroll={!centered} edges={['left', 'right', 'bottom']}>
      <Stack.Screen options={{ title: t.dogInvitation.screenTitle }} />
      {content}
    </Screen>
  );
}
