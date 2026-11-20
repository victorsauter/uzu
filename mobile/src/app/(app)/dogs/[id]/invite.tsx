import { Stack, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Pressable, Share, View } from 'react-native';

import { useCreateDogInvitation, useDog } from '@/api/dogs';
import { errorMessage } from '@/api/errors';
import { Badge, Button, ErrorState, Icon, LoadingState, Screen, Text } from '@/components';
import { useFocusedRouter } from '@/lib/hooks';
import { format, t } from '@/strings';
import { useTheme } from '@/theme';

interface RoleOption {
  key: 'owner' | 'trusted' | 'educator';
  title: string;
  hint: string;
  /** Roles of phase 2 (ADR 0042): shown, not selectable yet. */
  soon: boolean;
}

function RoleCard({
  option,
  selected,
  onSelect,
}: {
  option: RoleOption;
  selected: boolean;
  onSelect: () => void;
}) {
  const { colors, radii, spacing } = useTheme();
  const disabled = option.soon;
  return (
    <Pressable
      accessibilityRole="radio"
      accessibilityLabel={
        option.soon
          ? `${option.title}, ${t.dogInvitation.soon}. ${option.hint}`
          : `${option.title}. ${option.hint}`
      }
      accessibilityState={{ selected, disabled }}
      disabled={disabled}
      onPress={onSelect}
      style={{
        flexDirection: 'row',
        alignItems: 'center',
        gap: spacing.md,
        padding: spacing.md,
        borderRadius: radii.md,
        borderWidth: 1.5,
        borderColor: selected ? colors.primary : colors.border,
        backgroundColor: selected ? colors.primarySoft : colors.surface,
        opacity: disabled ? 0.55 : 1,
      }}
    >
      <View style={{ flex: 1, gap: 2 }}>
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: spacing.sm }}>
          <Text variant="subheading">{option.title}</Text>
          {option.soon ? <Badge label={t.dogInvitation.soon} /> : null}
        </View>
        <Text variant="caption" color="textMuted">
          {option.hint}
        </Text>
      </View>
      {selected ? <Icon name="check" size={20} color="primary" /> : null}
    </Pressable>
  );
}

/**
 * « Ajouter un humain à {nom} » (the title is the navigation bar's, as on
 * `edit-profile`, not repeated in the content): choose the role (only « Propriétaire » for now),
 * then create the invitation and hand its link to the system share sheet. The
 * link is created once: sharing again after the share sheet was dismissed reuses
 * it instead of piling up invitations.
 */
export default function InviteHumanScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { spacing } = useTheme();
  const dog = useDog(id);
  const create = useCreateDogInvitation(id);
  const router = useFocusedRouter();
  const [link, setLink] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [sharing, setSharing] = useState(false);

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

  const name = dog.data.name;
  const options: RoleOption[] = [
    {
      key: 'owner',
      title: t.dogInvitation.roleOwner,
      hint: format(t.dogInvitation.roleOwnerHint, { name }),
      soon: false,
    },
    {
      key: 'trusted',
      title: t.dogInvitation.roleTrusted,
      hint: t.dogInvitation.roleTrustedHint,
      soon: true,
    },
    {
      key: 'educator',
      title: t.dogInvitation.roleEducator,
      hint: t.dogInvitation.roleEducatorHint,
      soon: true,
    },
  ];

  async function share() {
    setError(null);
    setSharing(true);
    try {
      let url = link;
      if (!url) {
        const invitation = await create.mutateAsync('Owner');
        url = invitation.link;
        setLink(url);
      }
      const result = await Share.share({
        message: format(t.dogInvitation.shareMessage, { name, link: url }),
      });
      if (result.action === Share.sharedAction) router.back();
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setSharing(false);
    }
  }

  return (
    <Screen edges={['left', 'right', 'bottom']}>
      <Stack.Screen options={{ title: format(t.dogInvitation.sheetTitle, { name }) }} />
      <Text variant="heading">{t.dogInvitation.roleTitle}</Text>
      <View accessibilityRole="radiogroup" style={{ gap: spacing.sm }}>
        {options.map((option) => (
          <RoleCard
            key={option.key}
            option={option}
            selected={option.key === 'owner'}
            onSelect={() => {}}
          />
        ))}
      </View>
      <Button label={t.dogInvitation.share} loading={sharing} onPress={() => void share()} />
      {error ? (
        <Text color="danger" accessibilityLiveRegion="polite">
          {error}
        </Text>
      ) : null}
      <Text variant="caption" color="textMuted" align="center">
        {t.dogInvitation.help}
      </Text>
    </Screen>
  );
}
