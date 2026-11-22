import type { ConfigContext, ExpoConfig } from 'expo/config';
import { type ConfigPlugin, withEntitlementsPlist } from 'expo/config-plugins';

/**
 * Identity of the app (ADR 0041: the product is called Uzu; the repository and the
 * `dogo` folder keep their name). This is the main place to change when the app is
 * renamed (see "Construire une version de test" in mobile/README.md for what else
 * follows: the Apple audience of the API, the OAuth clients of Google, the store
 * listings; the deep link scheme is also written in `src/lib/appLinks.ts`, which a
 * test keeps equal to `urlScheme`). The bundle identifier and the package name are
 * confirmed, and frozen from the first build sent to TestFlight or Google Play.
 */
export const APP = {
  /** Name shown under the icon and in the permission texts. */
  displayName: 'Uzu',
  /** Expo slug, also the project name on expo.dev. */
  slug: 'uzu',
  /** Deep link scheme (`uzu://walks/<id>`), also used by push notification links. */
  urlScheme: 'uzu',
  /** Confirmed identifiers (ADR 0041), frozen from the first build sent to a store (spec §7). */
  iosBundleIdentifier: 'com.victorsauter.uzu',
  androidPackage: 'com.victorsauter.uzu',
} as const;

/**
 * Brand colours of the splash screen, the adaptive icon and notifications. They
 * repeat the « Uzu » tokens of `src/theme/tokens.ts` (ADR 0030); a test keeps
 * them equal.
 */
export const BRAND = {
  backgroundLight: '#FBF6EC',
  backgroundDark: '#14100D',
  primary: '#9C5626',
} as const;

/**
 * Permission texts shown by iOS and Android when the app asks for access.
 * Later features (map, photos, notifications) reuse these exact strings when
 * they add the matching Expo module config plugin.
 */
const permissions = {
  locationWhenInUse: `${APP.displayName} utilise ta position pour te montrer les balades proches de toi et placer un point de rendez-vous.`,
  photoLibrary: `${APP.displayName} accède à tes photos pour que tu choisisses ta photo de profil et celles de tes chiens.`,
  camera: `${APP.displayName} utilise l’appareil photo pour prendre ta photo de profil et celles de tes chiens.`,
} as const;

/** Environment variables read by the configuration (`process.env` in practice, injected in tests). */
export type ConfigEnv = Record<string, string | undefined>;

/**
 * EAS build profiles whose result is installed by people other than the
 * developer; the build fails early when their configuration cannot work.
 */
const DISTRIBUTION_PROFILES: readonly string[] = ['preview', 'production'];

/**
 * Stops a `preview` or `production` build that would ship a broken or unsafe
 * app: the API address must be given and use `https` (otherwise the app only
 * shows its « Application mal configurée » screen), and the test sign-in must
 * not be enabled. EAS Build sets `EAS_BUILD_PROFILE` to the profile name of
 * `eas.json` while it evaluates this file; local development and tests do not
 * have it, so they are never blocked.
 */
export function assertDistributionConfig(env: ConfigEnv): void {
  const profile = env.EAS_BUILD_PROFILE?.trim();
  if (!profile || !DISTRIBUTION_PROFILES.includes(profile)) return;

  const problems: string[] = [];
  const apiUrl = env.EXPO_PUBLIC_API_URL?.trim() ?? '';
  if (!apiUrl) {
    problems.push(
      'EXPO_PUBLIC_API_URL is missing: set it in the EAS environment variables of this profile (see mobile/README.md).',
    );
  } else if (!/^https:\/\/[^/\s]+/i.test(apiUrl)) {
    problems.push(
      `EXPO_PUBLIC_API_URL must start with https:// in a distribution build (got "${apiUrl}").`,
    );
  }
  if (env.EXPO_PUBLIC_DEV_LOGIN?.trim().toLowerCase() === 'true') {
    problems.push(
      'EXPO_PUBLIC_DEV_LOGIN=true must not be set in a distribution build: remove it from the EAS environment variables of this profile.',
    );
  }
  if (env.EXPO_PUBLIC_DEMO_MODE?.trim().toLowerCase() === 'true') {
    problems.push(
      'EXPO_PUBLIC_DEMO_MODE=true must not be set in a distribution build: it shows the test sign-in. Remove it from the EAS environment variables of this profile.',
    );
  }
  if (problems.length > 0) {
    throw new Error(
      `Invalid configuration for the EAS build profile "${profile}":\n- ${problems.join('\n- ')}`,
    );
  }
}

/**
 * Google sign-in needs custom native code (development build) and its config
 * plugin wants the iOS URL scheme, the reversed iOS client ID. Without the iOS
 * client ID the plugin is left out, so Expo Go and `expo export` keep working.
 */
function googleSignInPlugin(env: ConfigEnv): [string, { iosUrlScheme: string }][] {
  const iosClientId = env.EXPO_PUBLIC_GOOGLE_IOS_CLIENT_ID?.trim();
  const suffix = '.apps.googleusercontent.com';
  if (!iosClientId?.endsWith(suffix)) return [];
  const prefix = iosClientId.slice(0, -suffix.length);
  return [
    [
      '@react-native-google-signin/google-signin',
      { iosUrlScheme: `com.googleusercontent.apps.${prefix}` },
    ],
  ];
}

/** Android Maps key as the `react-native-maps` plugin wants it (Expo SDK 58 documentation). */
function googleMapsPlugin(
  apiKey: string | undefined,
): [string, { androidGoogleMapsApiKey: string }][] {
  return apiKey ? [['react-native-maps', { androidGoogleMapsApiKey: apiKey }]] : [];
}

/**
 * Plugins that need a capability a free Apple ID (Xcode personal team) cannot
 * sign: Sign in with Apple and push notifications. Left out with `UZU_FREE_SIGNING=true`.
 */
const FREE_SIGNING_EXCLUDED_PLUGINS: readonly string[] = [
  'expo-apple-authentication',
  'expo-notifications',
];

/** Entitlements a free Apple ID cannot sign (Sign in with Apple, push notifications). */
export const FREE_SIGNING_REMOVED_ENTITLEMENTS: readonly string[] = [
  'com.apple.developer.applesignin',
  'aps-environment',
];

/**
 * Removing the two plugins is not enough: Expo applies the plugin of an installed
 * package even when it is not listed ("legacy" plugins of `@expo/prebuild-config`),
 * so the entitlements come back. This plugin deletes them from the generated
 * entitlements file, after the others have written it.
 */
export const withoutPaidEntitlements: ConfigPlugin = (config) =>
  withEntitlementsPlist(config, (modConfig) => {
    for (const key of FREE_SIGNING_REMOVED_ENTITLEMENTS) delete modConfig.modResults[key];
    return modConfig;
  });

/**
 * Builds the Expo configuration from the environment. Everything that differs
 * between development, `preview` and `production` comes from variables (EAS
 * environment variables on a build, `.env` locally), never from the repository.
 */
export function createConfig({ config }: ConfigContext, env: ConfigEnv = process.env): ExpoConfig {
  assertDistributionConfig(env);

  /** EAS project ID, absent until an EAS project exists: push notifications are then unavailable. */
  const easProjectId = env.EXPO_PUBLIC_EAS_PROJECT_ID?.trim();
  /**
   * Google Maps key of the Android build, read at build time. Without it the map
   * stays grey outside Expo Go. It ends up readable in the app: restrict it in
   * the Google console (package name and certificate fingerprint).
   */
  const googleMapsAndroidApiKey = env.GOOGLE_MAPS_ANDROID_API_KEY?.trim();
  /**
   * Path of `google-services.json` (Firebase), needed by Android push
   * notifications. On EAS it is the path of an environment variable of type
   * file; locally it is usually unset.
   */
  const googleServicesFile = env.GOOGLE_SERVICES_JSON?.trim();
  /**
   * Demo build signed with a free Apple ID (ADR 0043): no Sign in with Apple, no
   * push notifications, which that kind of signing cannot carry.
   */
  const freeSigning = env.UZU_FREE_SIGNING?.trim().toLowerCase() === 'true';
  /** Replaces the iOS bundle identifier when Xcode refuses it to a personal team; default unchanged. */
  const iosBundleIdentifier = env.UZU_IOS_BUNDLE_IDENTIFIER?.trim() || APP.iosBundleIdentifier;
  const extra = {
    ...config.extra,
    ...(easProjectId ? { eas: { projectId: easProjectId } } : {}),
    ...(freeSigning ? { freeSigning: true } : {}),
  };
  const plugins: NonNullable<ExpoConfig['plugins']> = [
    'expo-router',
    'expo-secure-store',
    'expo-apple-authentication',
    [
      'expo-image-picker',
      {
        photosPermission: permissions.photoLibrary,
        cameraPermission: permissions.camera,
        microphonePermission: false,
      },
    ],
    '@react-native-community/datetimepicker',
    // Apple Maps on iOS needs no key; Android uses Google Maps (Expo Go's key in
    // development, `GOOGLE_MAPS_ANDROID_API_KEY` for a build, see mobile/README.md).
    ['expo-location', { locationWhenInUsePermission: permissions.locationWhenInUse }],
    // The plugin of react-native-maps is added only with a key: without a key it would
    // remove any Maps key from the Android manifest.
    ...googleMapsPlugin(googleMapsAndroidApiKey),
    [
      'expo-notifications',
      {
        defaultChannel: 'default',
        // Android only: all-white silhouette on a transparent background, tinted with the brand colour.
        icon: './assets/notification-icon.png',
        color: BRAND.primary,
      },
    ],
    ...googleSignInPlugin(env),
    [
      'expo-splash-screen',
      {
        image: './assets/splash-icon.png',
        imageWidth: 160,
        resizeMode: 'contain',
        backgroundColor: BRAND.backgroundLight,
        dark: { backgroundColor: BRAND.backgroundDark },
      },
    ],
  ];

  const expoConfig: ExpoConfig = {
    ...config,
    name: APP.displayName,
    slug: APP.slug,
    scheme: APP.urlScheme,
    platforms: ['ios', 'android'],
    version: '0.1.0',
    orientation: 'portrait',
    icon: './assets/icon.png',
    userInterfaceStyle: 'automatic',
    ios: {
      bundleIdentifier: iosBundleIdentifier,
      supportsTablet: false,
      usesAppleSignIn: !freeSigning,
      config: {
        // The app only uses the encryption of the system (HTTPS): no export question at each TestFlight upload.
        usesNonExemptEncryption: false,
      },
      infoPlist: {
        NSLocationWhenInUseUsageDescription: permissions.locationWhenInUse,
        NSPhotoLibraryUsageDescription: permissions.photoLibrary,
        NSCameraUsageDescription: permissions.camera,
      },
    },
    android: {
      package: APP.androidPackage,
      adaptiveIcon: {
        backgroundColor: BRAND.backgroundLight,
        foregroundImage: './assets/android-icon-foreground.png',
        monochromeImage: './assets/android-icon-monochrome.png',
      },
      permissions: [
        'android.permission.ACCESS_COARSE_LOCATION',
        'android.permission.ACCESS_FINE_LOCATION',
        'android.permission.CAMERA',
        'android.permission.POST_NOTIFICATIONS',
      ],
      predictiveBackGestureEnabled: false,
      ...(googleServicesFile ? { googleServicesFile } : {}),
    },
    web: {
      favicon: './assets/favicon.png',
    },
    ...(easProjectId || freeSigning ? { extra } : {}),
    plugins: freeSigning
      ? plugins.filter(
          (plugin) =>
            !FREE_SIGNING_EXCLUDED_PLUGINS.includes(
              String(Array.isArray(plugin) ? plugin[0] : plugin),
            ),
        )
      : plugins,
    experiments: {
      typedRoutes: true,
    },
  };
  return freeSigning ? withoutPaidEntitlements(expoConfig) : expoConfig;
}

export default (context: ConfigContext): ExpoConfig => createConfig(context);
