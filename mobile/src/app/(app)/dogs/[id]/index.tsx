import { Stack, useLocalSearchParams } from 'expo-router';
import { useRef, useState } from 'react';
import { Alert, Pressable, View, type ScrollViewInstance } from 'react-native';

import { useGuardedRouter } from '@/lib/navigation';
import {
  useDeleteDog,
  useDog,
  useDogInvitations,
  useLeaveDog,
  useRevokeDogInvitation,
} from '@/api/dogs';
import { errorMessage } from '@/api/errors';
import { useSession } from '@/auth';
import {
  Avatar,
  AvatarStack,
  Badge,
  Button,
  Card,
  ErrorState,
  Icon,
  ListItem,
  LoadingState,
  Screen,
  Text,
} from '@/components';
import { formatAge } from '@/lib/age';
import { formatDate } from '@/lib/dates';
import { humanNames, isDogOwner, listNames, withHumansPhrase } from '@/lib/dogMembers';
import { useFocusedRouter } from '@/lib/hooks';
import { reportHref } from '@/lib/report';
import { enumLabel, format, t } from '@/strings';
import { useTheme } from '@/theme';

const DOG_AVATAR = 120;

function Row({ label, value }: { label: string; value: string }) {
  return (
    // Two per line: half of the card's width, the labels wrap rather than push the other column.
    <View accessible accessibilityLabel={`${label} : ${value}`} style={{ width: '48%' }}>
      <Text variant="caption" color="textMuted">
        {label}
      </Text>
      <Text>{value}</Text>
    </View>
  );
}

/** Read-only profile of a dog; the owner also gets edit and delete. */
export default function DogDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { user } = useSession();
  const { colors, spacing } = useTheme();
  const router = useGuardedRouter();
  const scrollRef = useRef<ScrollViewInstance>(null);
  // Top of the « Ses humains » section in the scroll content, measured at layout.
  const humansTop = useRef(0);
  const focusedRouter = useFocusedRouter();
  const dog = useDog(id);
  const deleteDog = useDeleteDog();
  const leaveDog = useLeaveDog(id);
  const revokeInvitation = useRevokeDogInvitation(id);
  const owner = dog.data ? isDogOwner(dog.data, user?.id) : false;
  const invitations = useDogInvitations(id, owner);
  // Refusal of delete, leave or cancel-invitation: the state may have changed meanwhile (409).
  const [actionError, setActionError] = useState<string | null>(null);

  if (dog.isPending)
    return (
      <Screen scroll={false}>
        <LoadingState />
      </Screen>
    );
  if (dog.isError) {
    return (
      <Screen scroll={false}>
        <ErrorState
          error={dog.error}
          notFoundTitle={t.dog.notFoundTitle}
          onRetry={() => dog.refetch()}
        />
      </Screen>
    );
  }

  const data = dog.data;
  const mine = isDogOwner(data, user?.id);
  const users = data.members.map((member) => member.user);
  const names = humanNames(data.members, user?.id);
  const showHumans = () =>
    scrollRef.current?.scrollTo({ y: Math.max(0, humansTop.current - spacing.lg), animated: true });

  const owners = data.members.filter((member) => member.role === 'Owner');
  // Not the last owner: the dog stays with the others, so « Ne plus m'occuper » replaces « Supprimer ».
  const lastOwner = owners.length <= 1;
  const otherOwners = humanNames(
    owners.filter((member) => member.user.id !== user?.id),
    user?.id,
  );

  function confirmDelete() {
    setActionError(null);
    Alert.alert(format(t.dog.deleteConfirmTitle, { name: data.name }), t.dog.deleteConfirmMessage, [
      { text: t.common.cancel, style: 'cancel' },
      {
        text: t.common.delete,
        style: 'destructive',
        onPress: () => {
          deleteDog.mutate(data.id, {
            onSuccess: () => focusedRouter.back(),
            onError: (error) => setActionError(errorMessage(error)),
          });
        },
      },
    ]);
  }

  function confirmLeave() {
    setActionError(null);
    Alert.alert(
      format(t.dog.leaveConfirmTitle, { name: data.name }),
      format(t.dog.leaveConfirmMessage, {
        name: data.name,
        others: listNames(otherOwners, 3),
      }),
      [
        { text: t.common.cancel, style: 'cancel' },
        {
          text: t.dog.leaveConfirmAction,
          style: 'destructive',
          onPress: () => {
            leaveDog.mutate(undefined, {
              onSuccess: () => focusedRouter.back(),
              onError: (error) => setActionError(errorMessage(error)),
            });
          },
        },
      ],
    );
  }

  function confirmCancelInvitation(invitationId: string) {
    setActionError(null);
    Alert.alert(t.dog.cancelInvitationTitle, t.dog.cancelInvitationMessage, [
      { text: t.dog.cancelInvitationKeep, style: 'cancel' },
      {
        text: t.dog.cancelInvitation,
        style: 'destructive',
        onPress: () => {
          revokeInvitation.mutate(invitationId, {
            onError: (error) => setActionError(errorMessage(error)),
          });
        },
      },
    ]);
  }

  return (
    <Screen edges={['left', 'right', 'bottom']} scrollRef={scrollRef}>
      <Stack.Screen options={{ title: data.name }} />
      <View style={{ alignItems: 'center', gap: spacing.sm }}>
        <View style={{ width: DOG_AVATAR, height: DOG_AVATAR }}>
          <Avatar name={data.name} photoUrl={data.photoUrl} size={DOG_AVATAR} />
          {users.length > 0 ? (
            <View style={{ position: 'absolute', right: -spacing.sm, bottom: -spacing.xs }}>
              <AvatarStack
                testID="dog-humans-stack"
                users={users}
                accessibilityLabel={format(t.dog.humansOf, {
                  name: data.name,
                  names: listNames(
                    users.map((human) => human.displayName),
                    users.length,
                  ),
                })}
                accessibilityHint={t.dog.humansScrollHint}
                onPress={showHumans}
              />
            </View>
          ) : null}
        </View>
        <Text variant="title" align="center">
          {data.name}
        </Text>
        <Text color="textMuted" align="center">
          {data.breed || t.dog.noBreed}
        </Text>
        {names.length > 0 ? (
          <Pressable
            accessibilityRole="button"
            accessibilityHint={t.dog.humansScrollHint}
            onPress={showHumans}
            hitSlop={8}
          >
            <Text color="primary" align="center" style={{ fontWeight: '600' }}>
              {withHumansPhrase(data.members, user?.id)}
            </Text>
          </Pressable>
        ) : null}
      </View>

      <Text variant="heading">{t.dog.characterTitle}</Text>
      <Card
        style={{
          flexDirection: 'row',
          flexWrap: 'wrap',
          justifyContent: 'space-between',
          rowGap: spacing.md,
        }}
      >
        <Row label={t.dog.size} value={enumLabel('DogSize', data.size)} />
        <Row label={t.dog.sociability} value={enumLabel('Sociability', data.sociability)} />
        <Row label={t.dog.reactivity} value={enumLabel('Reactivity', data.reactivity)} />
        <Row label={t.dog.leashHabit} value={enumLabel('LeashHabit', data.leashHabit)} />
        <Row label={t.dog.age} value={formatAge(data.birthDate) ?? t.dog.noBirthDate} />
      </Card>

      <Text variant="heading">{t.dog.descriptionTitle}</Text>
      <Card>
        <Text>{data.description || t.dog.noDescription}</Text>
      </Card>

      <View
        style={{ gap: spacing.lg }}
        onLayout={(event) => {
          humansTop.current = event.nativeEvent.layout.y;
        }}
      >
        <Text variant="heading">{t.dog.humansTitle}</Text>
        <Card style={{ padding: 0, overflow: 'hidden', gap: 0 }}>
          {data.members.map((member, index) => {
            const itsMe = member.user.id === user?.id;
            const role = enumLabel('DogMemberRole', member.role);
            const name = itsMe ? t.dog.you : member.user.displayName;
            return (
              <View
                key={member.user.id}
                style={index > 0 ? { borderTopWidth: 1, borderTopColor: colors.border } : undefined}
              >
                <ListItem
                  title={name}
                  accessibilityLabel={format(t.dog.memberA11y, { name, role })}
                  left={
                    <Avatar
                      name={member.user.displayName}
                      photoUrl={member.user.photoUrl}
                      size={40}
                    />
                  }
                  below={<Badge label={role} tone="primary" />}
                  right={itsMe ? undefined : <Icon name="chevron" size={16} color="textMuted" />}
                  onPress={
                    itsMe
                      ? undefined
                      : () =>
                          router.push({
                            pathname: '/users/[id]',
                            params: { id: member.user.id },
                          })
                  }
                />
              </View>
            );
          })}
          {mine
            ? (invitations.data ?? []).map((invitation) => {
                const text = format(t.dog.pendingInvitation, {
                  date: formatDate(invitation.expiresAt),
                });
                return (
                  <View
                    key={invitation.id}
                    style={{
                      borderTopWidth: 1,
                      borderTopColor: colors.border,
                      flexDirection: 'row',
                      alignItems: 'center',
                      gap: spacing.md,
                      paddingVertical: spacing.sm,
                      paddingHorizontal: spacing.lg,
                    }}
                  >
                    <Text variant="caption" color="textMuted" style={{ flex: 1 }}>
                      {text}
                    </Text>
                    <Pressable
                      accessibilityRole="button"
                      accessibilityLabel={`${t.dog.cancelInvitation}, ${text}`}
                      disabled={revokeInvitation.isPending}
                      hitSlop={8}
                      onPress={() => confirmCancelInvitation(invitation.id)}
                    >
                      <Text variant="caption" color="primary" style={{ fontWeight: '600' }}>
                        {t.dog.cancelInvitation}
                      </Text>
                    </Pressable>
                  </View>
                );
              })
            : null}
          {mine ? (
            <Pressable
              accessibilityRole="button"
              accessibilityLabel={format(t.dog.addHumanA11y, { name: data.name })}
              onPress={() =>
                router.push({ pathname: '/dogs/[id]/invite', params: { id: data.id } })
              }
              style={({ pressed }) => ({
                minHeight: 56,
                flexDirection: 'row',
                alignItems: 'center',
                gap: spacing.md,
                paddingVertical: spacing.sm,
                paddingHorizontal: spacing.lg,
                borderTopWidth: 1,
                borderTopColor: colors.border,
                backgroundColor: pressed ? colors.surfaceMuted : 'transparent',
              })}
            >
              <View
                style={{
                  width: 40,
                  height: 40,
                  borderRadius: 20,
                  borderWidth: 1.5,
                  borderStyle: 'dashed',
                  borderColor: colors.primary,
                  alignItems: 'center',
                  justifyContent: 'center',
                }}
              >
                <Icon name="plus" size={18} color="primary" />
              </View>
              <Text variant="subheading" color="primary">
                {t.dog.addHuman}
              </Text>
            </Pressable>
          ) : null}
        </Card>
      </View>

      {!mine ? (
        <View style={{ gap: spacing.md }}>
          <Button
            label={t.report.action}
            variant="secondary"
            onPress={() => router.push(reportHref('dog', data.id, data.name))}
          />
        </View>
      ) : null}
      {mine ? (
        <View style={{ gap: spacing.md }}>
          <Button
            label={t.common.edit}
            onPress={() => router.push({ pathname: '/dogs/[id]/edit', params: { id: data.id } })}
          />
          {lastOwner ? (
            <Button
              label={t.common.delete}
              variant="danger"
              loading={deleteDog.isPending}
              onPress={confirmDelete}
            />
          ) : (
            <Button
              label={format(t.dog.leave, { name: data.name })}
              variant="secondary"
              loading={leaveDog.isPending}
              onPress={confirmLeave}
            />
          )}
          {actionError ? (
            <Text color="danger" accessibilityLiveRegion="polite">
              {actionError}
            </Text>
          ) : null}
        </View>
      ) : null}
    </Screen>
  );
}
