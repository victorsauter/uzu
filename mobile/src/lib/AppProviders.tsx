import { QueryClientProvider } from '@tanstack/react-query';
import { SplashScreen } from 'expo-router';
import { useCallback, useEffect, useState, type PropsWithChildren } from 'react';
import { GestureHandlerRootView } from 'react-native-gesture-handler';
import { SafeAreaProvider } from 'react-native-safe-area-context';

import { connectQueryEventsToReactNative, createQueryClient } from '@/api/queryClient';
import { SessionProvider } from '@/auth';
import { Screen, Text } from '@/components';
import { unregisterDevice } from '@/notifications';
import { t } from '@/strings';
import { ThemeProvider } from '@/theme';

import { config, type ConfigError } from './config';
import { clearPendingInvitation } from './pendingInvitation';

/**
 * Every app-wide provider, in dependency order. Used by the root layout.
 * `GestureHandlerRootView` comes first: the sliding panel of the « Balades »
 * tab (`@gorhom/bottom-sheet`) needs gesture handling from the app's root.
 *
 * A misconfigured distribution build (API address missing or not https, see
 * `config.error`) shows `ConfigErrorScreen` instead of the app.
 */
export function AppProviders({
  children,
  configError = config.error,
}: PropsWithChildren<{ configError?: ConfigError }>) {
  const [queryClient] = useState(createQueryClient);

  useEffect(() => connectQueryEventsToReactNative(), []);

  // Cached data belongs to the previous user; so does an invitation link kept for after a sign-in.
  const clearCache = useCallback(() => {
    queryClient.clear();
    clearPendingInvitation();
  }, [queryClient]);

  return (
    <GestureHandlerRootView style={{ flex: 1 }}>
      <SafeAreaProvider>
        <ThemeProvider>
          {configError ? (
            <ConfigErrorScreen error={configError} />
          ) : (
            <QueryClientProvider client={queryClient}>
              <SessionProvider onBeforeSignOut={unregisterDevice} onSignedOut={clearCache}>
                {children}
              </SessionProvider>
            </QueryClientProvider>
          )}
        </ThemeProvider>
      </SafeAreaProvider>
    </GestureHandlerRootView>
  );
}

const configErrorMessage: Record<ConfigError, string> = {
  missingApiUrl: t.errors.configMissingApiUrl,
  insecureApiUrl: t.errors.configInsecureApiUrl,
};

/** Shown at launch, instead of an app that cannot reach anything. */
function ConfigErrorScreen({ error }: { error: ConfigError }) {
  // The root layout hides the splash screen once the session is read, which never happens here.
  useEffect(() => {
    SplashScreen.hide();
  }, []);

  return (
    <Screen scroll={false} contentStyle={{ justifyContent: 'center' }}>
      <Text variant="title" align="center" accessibilityRole="header">
        {t.errors.configTitle}
      </Text>
      <Text color="textMuted" align="center">
        {configErrorMessage[error]}
      </Text>
    </Screen>
  );
}
