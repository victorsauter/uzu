import { useState } from 'react';
import { Alert, Pressable, View } from 'react-native';

import { useMyDogs } from '@/api/dogs';
import { ApiError, errorMessage } from '@/api/errors';
import type { Dog, UserSummary, WalkDetail } from '@/api/types';
import { useCancelRegistration, useRegisterDogs } from '@/api/walks';
import { useSession } from '@/auth';
import { offerNotifications } from '@/notifications';
import { enumLabel, format, plural, t } from '@/strings';
import { useTheme } from '@/theme';

import { Avatar } from './Avatar';
import { Badge } from './Badge';
import { Button } from './Button';
import { Card } from './Card';
import { ErrorState } from './ErrorState';
import { Icon } from './Icon';
import { LoadingState } from './LoadingState';
import { Text } from './Text';
import { isWalkFull } from './WalkCard';

export interface WalkRegistrationPanelProps {
  walk: WalkDetail;
  /** Registrations are open: the walk is scheduled and has not started. */
  open: boolean;
  onAddDog: () => void;
  /** Opens the organisation page (professional walks reserved to members). */
  onOpenOrganization?: (organizationId: string) => void;
}

const isActive = (status: string) => status === 'Pending' || status === 'Accepted';

/**
 * The registration of one of my dogs made by another of its owners
 * (`myDogsRegistrations`, ADR 0042), or `undefined`. Mine are in `myRegistrations`.
 */
function registrationByOtherOwner(dogId: string, walk: WalkDetail, meId: string | undefined) {
  return walk.myDogsRegistrations.find(
    (r) => r.dogId === dogId && isActive(r.status) && r.registeredBy.id !== meId,
  );
}

/** Why a dog cannot be picked, or `null` when it can. */
export function dogUnavailableReason(dog: Dog, walk: WalkDetail, meId?: string): string | null {
  const byOther = registrationByOtherOwner(dog.id, walk, meId);
  if (byOther) {
    return format(
      byOther.status === 'Accepted' ? t.walk.alreadyRegisteredBy : t.walk.alreadyPendingBy,
      { name: byOther.registeredBy.displayName },
    );
  }
  const registration = walk.myRegistrations.find((r) => r.dogId === dog.id && isActive(r.status));
  if (registration?.status === 'Accepted') return t.walk.alreadyRegistered;
  if (registration?.status === 'Pending') return t.walk.alreadyPending;
  if (!walk.allowedSizes.includes(dog.size)) {
    return format(t.walk.sizeNotAllowed, { size: enumLabel('DogSize', dog.size) });
  }
  return null;
}

function DogChoice({
  dog,
  selected,
  reason,
  onToggle,
}: {
  dog: Dog;
  selected: boolean;
  reason: string | null;
  onToggle: () => void;
}) {
  const { colors, radii, spacing } = useTheme();
  const disabled = reason !== null;
  return (
    <Pressable
      accessibilityRole="checkbox"
      accessibilityLabel={reason ? `${dog.name}, ${reason}` : dog.name}
      accessibilityState={{ checked: selected, disabled }}
      disabled={disabled}
      onPress={onToggle}
      style={({ pressed }) => ({
        flexDirection: 'row',
        alignItems: 'center',
        gap: spacing.md,
        padding: spacing.md,
        borderRadius: radii.md,
        borderWidth: 1.5,
        borderColor: selected ? colors.primary : colors.border,
        backgroundColor: selected
          ? colors.primarySoft
          : pressed
            ? colors.surfaceMuted
            : colors.surface,
        opacity: disabled ? 0.55 : 1,
      })}
    >
      <Avatar name={dog.name} photoUrl={dog.photoUrl} size={44} placeholderIcon="paw" />
      <View style={{ flex: 1 }}>
        <Text variant="subheading">{dog.name}</Text>
        <Text variant="caption" color={reason ? 'warning' : 'textMuted'}>
          {reason ?? enumLabel('DogSize', dog.size)}
        </Text>
      </View>
      {selected ? <Icon name="paw" size={20} color="primary" /> : null}
    </Pressable>
  );
}

/**
 * Registration on a walk: my registrations with their status (cancellable),
 * and the choice of my dogs to register. Dogs whose size is not allowed or
 * that are already registered are shown disabled with the reason.
 */
export function WalkRegistrationPanel({
  walk,
  open,
  onAddDog,
  onOpenOrganization,
}: WalkRegistrationPanelProps) {
  const { spacing } = useTheme();
  const { user } = useSession();
  const dogs = useMyDogs();
  const register = useRegisterDogs(walk.id);
  const cancel = useCancelRegistration(walk.id);
  const [selected, setSelected] = useState<string[]>([]);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [membersOnly, setMembersOnly] = useState(false);
  const full = isWalkFull(walk);

  const dogName = (dogId: string | null) =>
    dogs.data?.find((dog) => dog.id === dogId)?.name ??
    walk.participants.find((p) => p.dog.id === dogId)?.dog.name ??
    t.walk.deletedDog;

  function toggle(id: string) {
    setMessage(null);
    setError(null);
    setSelected((current) =>
      current.includes(id) ? current.filter((item) => item !== id) : [...current, id],
    );
  }

  function submit() {
    setMessage(null);
    setError(null);
    setMembersOnly(false);
    register.mutate(selected, {
      onSuccess: (registrations) => {
        setSelected([]);
        const pending = registrations.some((r) => r.status === 'Pending');
        setMessage(pending ? t.walk.registeredApproval : t.walk.registeredOpen);
        // Meaningful moment to offer notifications (asked once, never when unavailable).
        void offerNotifications();
      },
      onError: (e) => {
        if (e instanceof ApiError && e.code === 'walk.members_only') setMembersOnly(true);
        else setError(errorMessage(e));
      },
    });
  }

  function confirmCancel(registrationId: string, name: string, madeBy: UserSummary | null) {
    setMessage(null);
    setError(null);
    Alert.alert(
      format(t.walk.cancelRegistrationTitle, { name }),
      madeBy
        ? format(t.walk.cancelRegistrationOfOtherMessage, { name: madeBy.displayName })
        : t.walk.cancelRegistrationMessage,
      [
        { text: t.walk.keep, style: 'cancel' },
        {
          text: t.walk.cancelRegistration,
          style: 'destructive',
          onPress: () =>
            cancel.mutate(registrationId, { onError: (e) => setError(errorMessage(e)) }),
        },
      ],
    );
  }

  // My registrations, then those another owner of one of my dogs made (shown « par {prénom} »).
  const mine = [
    ...walk.myRegistrations.map((r) => ({ ...r, madeBy: null as UserSummary | null })),
    ...walk.myDogsRegistrations
      .filter(
        (r) => r.registeredBy.id !== user?.id && !walk.myRegistrations.some((m) => m.id === r.id),
      )
      .map((r) => ({ id: r.id, dogId: r.dogId, status: r.status, madeBy: r.registeredBy })),
  ];
  const errorText = error ? (
    <Text color="danger" accessibilityLiveRegion="polite">
      {error}
    </Text>
  ) : null;
  return (
    <View style={{ gap: spacing.md }}>
      {mine.length > 0 ? (
        <Card>
          <Text variant="heading">{t.walk.myRegistrations}</Text>
          {mine.map((registration) => {
            const name = dogName(registration.dogId);
            return (
              <View key={registration.id} style={{ gap: spacing.xs }}>
                <View style={{ flexDirection: 'row', alignItems: 'center', gap: spacing.sm }}>
                  <View style={{ flex: 1 }}>
                    <Text variant="subheading">{name}</Text>
                    {registration.madeBy ? (
                      <Text variant="caption" color="textMuted">
                        {format(t.walk.registeredBy, { name: registration.madeBy.displayName })}
                      </Text>
                    ) : null}
                  </View>
                  <Badge
                    tone={
                      registration.status === 'Accepted'
                        ? 'primary'
                        : registration.status === 'Pending'
                          ? 'info'
                          : 'neutral'
                    }
                    label={enumLabel('RegistrationStatus', registration.status)}
                  />
                </View>
                {open && isActive(registration.status) ? (
                  <Button
                    label={t.walk.cancelRegistration}
                    accessibilityLabel={`${t.walk.cancelRegistration} : ${name}`}
                    variant="secondary"
                    loading={cancel.isPending && cancel.variables === registration.id}
                    onPress={() => confirmCancel(registration.id, name, registration.madeBy)}
                  />
                ) : null}
              </View>
            );
          })}
        </Card>
      ) : null}

      {open ? (
        <Card>
          <Text variant="heading">{t.walk.registerTitle}</Text>
          {dogs.isPending ? (
            <LoadingState />
          ) : dogs.isError ? (
            <ErrorState error={dogs.error} onRetry={() => dogs.refetch()} />
          ) : dogs.data.length === 0 ? (
            <View style={{ gap: spacing.sm }}>
              <Text variant="subheading">{t.walk.noDogTitle}</Text>
              <Text color="textMuted">{t.walk.noDogMessage}</Text>
              <Button label={t.walk.addDog} variant="secondary" onPress={onAddDog} />
            </View>
          ) : full ? (
            <Text color="textMuted">{t.walk.fullHint}</Text>
          ) : (
            <View style={{ gap: spacing.sm }}>
              <Text color="textMuted">{t.walk.registerHint}</Text>
              {dogs.data.map((dog) => (
                <DogChoice
                  key={dog.id}
                  dog={dog}
                  selected={selected.includes(dog.id)}
                  reason={dogUnavailableReason(dog, walk, user?.id)}
                  onToggle={() => toggle(dog.id)}
                />
              ))}
              <Button
                label={
                  selected.length === 0
                    ? t.walk.registerPick
                    : plural(t.walk.registerButton, selected.length)
                }
                disabled={selected.length === 0}
                loading={register.isPending}
                onPress={submit}
              />
            </View>
          )}
          {message ? (
            <Text color="success" accessibilityLiveRegion="polite">
              {message}
            </Text>
          ) : null}
          {/* Right under the button: a refusal below the card was off screen. */}
          {membersOnly ? (
            <View style={{ gap: spacing.sm }}>
              <Text variant="subheading">{t.walk.membersOnlyTitle}</Text>
              <Text accessibilityLiveRegion="polite">{t.walk.membersOnlyMessage}</Text>
              {walk.organization && onOpenOrganization ? (
                <Button
                  label={t.walk.membersOnlyAction}
                  onPress={() => onOpenOrganization(walk.organization!.id)}
                />
              ) : null}
            </View>
          ) : null}
          {errorText}
        </Card>
      ) : (
        errorText
      )}
    </View>
  );
}
