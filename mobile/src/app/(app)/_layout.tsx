import { Stack } from 'expo-router';
import { useEffect } from 'react';

import { useGuardedRouter } from '@/lib/navigation';
import { useSession } from '@/auth';
import { takePendingInvitation, usePendingInvitation } from '@/lib/pendingInvitation';
import { isWelcomeInProgress, shouldShowWelcome, useWelcomeInProgress } from '@/lib/welcomeNudge';
import { t } from '@/strings';
import { useTheme } from '@/theme';

/**
 * Signed-in area: the tabs, and the screens pushed on top of them (edit
 * profile, public profile, dogs, walks, organisations, reports, account). The
 * screens are declared in the order Expo Router sorts the route files (groups,
 * then static routes by name length, then dynamic ones): any other order makes
 * the navigator report a « not handled » ROUTE_NAMES_CHANGED action (ADR 0026).
 * Also opens the edit-profile screen once for
 * a new member who still has the default display name, and the screen of an
 * invitation link that arrived before the sign-in (`pendingInvitation`): at
 * once for a known member, after the welcome screen for a new one (which
 * hands it over itself, see `edit-profile`).
 */
export default function AppLayout() {
  const { colors } = useTheme();
  const { user } = useSession();
  const router = useGuardedRouter();

  useEffect(() => {
    if (user && shouldShowWelcome(user)) {
      router.push({ pathname: '/edit-profile', params: { welcome: '1' } });
    }
  }, [user, router]);

  // The link is opened once: taking it forgets it, so « Plus tard » on the invitation does not bring it back.
  const invitationWaiting = usePendingInvitation();
  const welcomeOpen = useWelcomeInProgress();
  useEffect(() => {
    if (!user || !invitationWaiting || welcomeOpen || isWelcomeInProgress()) return;
    const token = takePendingInvitation();
    if (token) router.push({ pathname: '/invitation/[token]', params: { token } });
  }, [user, invitationWaiting, welcomeOpen, router]);

  return (
    <Stack
      screenOptions={{
        headerStyle: { backgroundColor: colors.surface },
        headerTintColor: colors.primary,
        headerTitleStyle: { color: colors.text },
        headerBackButtonDisplayMode: 'minimal',
        contentStyle: { backgroundColor: colors.background },
      }}
    >
      <Stack.Screen name="(tabs)" options={{ headerShown: false }} />
      <Stack.Screen name="report" options={{ title: t.report.title }} />
      <Stack.Screen name="blocked" options={{ title: t.block.listTitle }} />
      <Stack.Screen name="dogs/new" options={{ title: t.dog.addTitle }} />
      <Stack.Screen name="walks/new" options={{ title: t.walk.newTitle }} />
      <Stack.Screen
        name="edit-profile"
        options={{ title: t.profile.editTitle, presentation: 'modal' }}
      />
      <Stack.Screen name="notifications" options={{ title: t.notifications.title }} />
      <Stack.Screen name="delete-account" options={{ title: t.deleteAccount.title }} />
      <Stack.Screen name="organizations/new" options={{ title: t.organization.createTitle }} />
      <Stack.Screen name="organizations/index" options={{ title: t.organization.findTitle }} />
      <Stack.Screen name="dogs/[id]/edit" options={{ title: t.dog.editTitle }} />
      <Stack.Screen name="dogs/[id]/index" options={{ title: t.dog.detailTitle }} />
      <Stack.Screen
        name="dogs/[id]/invite"
        options={{ title: t.dogInvitation.screenTitle, presentation: 'modal' }}
      />
      <Stack.Screen name="invitation/[token]" options={{ title: t.dogInvitation.screenTitle }} />
      <Stack.Screen name="organizations/[id]/edit" options={{ title: t.organization.editTitle }} />
      <Stack.Screen
        name="organizations/[id]/index"
        options={{ title: t.organization.detailTitle }}
      />
      <Stack.Screen
        name="organizations/[id]/members"
        options={{ title: t.organization.membersTitle }}
      />
      <Stack.Screen name="users/[id]" options={{ title: t.profile.publicTitle }} />
      <Stack.Screen name="walks/[id]/edit" options={{ title: t.walk.editTitle }} />
      <Stack.Screen name="walks/[id]/index" options={{ title: t.walk.detailTitle }} />
      <Stack.Screen name="walks/[id]/messages" options={{ title: t.thread.title }} />
    </Stack>
  );
}
