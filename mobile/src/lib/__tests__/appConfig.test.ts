import type { ConfigContext } from 'expo/config';

import appConfig, {
  APP,
  BRAND,
  BRAND_ASSETS,
  assertDistributionConfig,
  createConfig,
} from '../../../app.config';
import { SESSION_STORAGE_KEY } from '../../auth/sessionStore';
import { DISCOVER_LAYOUT_STORAGE_KEY } from '../../discover/layoutPreference';
import { fr } from '../../strings/fr';
import { darkColors, lightColors } from '../../theme/tokens';
import { APP_URL_SCHEME } from '../appLinks';

const context = { config: {} } as ConfigContext;

/** A complete, valid `production` environment. */
const production = {
  EAS_BUILD_PROFILE: 'production',
  EXPO_PUBLIC_API_URL: 'https://api.test',
};

describe('assertDistributionConfig', () => {
  it.each([{}, { EXPO_PUBLIC_API_URL: 'http://localhost:5080', EXPO_PUBLIC_DEV_LOGIN: 'true' }])(
    'lets local development and tests through, whatever the variables (%j)',
    (env) => {
      expect(() => assertDistributionConfig(env)).not.toThrow();
    },
  );

  it.each(['development', 'development-simulator'])(
    'does not check the %s profile, a development client talks to a local API',
    (profile) => {
      expect(() =>
        assertDistributionConfig({
          EAS_BUILD_PROFILE: profile,
          EXPO_PUBLIC_API_URL: 'http://192.168.1.20:5080',
          EXPO_PUBLIC_DEV_LOGIN: 'true',
        }),
      ).not.toThrow();
    },
  );

  it.each(['preview', 'production'])('accepts an https API address in %s', (profile) => {
    expect(() =>
      assertDistributionConfig({ ...production, EAS_BUILD_PROFILE: profile }),
    ).not.toThrow();
  });

  it.each(['preview', 'production'])('rejects a missing API address in %s', (profile) => {
    expect(() => assertDistributionConfig({ EAS_BUILD_PROFILE: profile })).toThrow(
      /"?EXPO_PUBLIC_API_URL is missing/,
    );
    expect(() =>
      assertDistributionConfig({ EAS_BUILD_PROFILE: profile, EXPO_PUBLIC_API_URL: '  ' }),
    ).toThrow(/EXPO_PUBLIC_API_URL is missing/);
  });

  it.each(['http://api.test', 'http://localhost:5080', 'api.test', 'ftp://api.test', 'https://'])(
    'rejects the API address %p, which is not https',
    (url) => {
      expect(() => assertDistributionConfig({ ...production, EXPO_PUBLIC_API_URL: url })).toThrow(
        /must start with https:\/\//,
      );
    },
  );

  it('accepts an https address in any letter case, with spaces around it', () => {
    expect(() =>
      assertDistributionConfig({ ...production, EXPO_PUBLIC_API_URL: ' HTTPS://api.test/ ' }),
    ).not.toThrow();
  });

  it.each(['true', 'TRUE', ' true '])('rejects EXPO_PUBLIC_DEV_LOGIN=%p in production', (value) => {
    expect(() => assertDistributionConfig({ ...production, EXPO_PUBLIC_DEV_LOGIN: value })).toThrow(
      /EXPO_PUBLIC_DEV_LOGIN=true must not be set/,
    );
  });

  it('accepts EXPO_PUBLIC_DEV_LOGIN when it is not true', () => {
    expect(() =>
      assertDistributionConfig({ ...production, EXPO_PUBLIC_DEV_LOGIN: 'false' }),
    ).not.toThrow();
  });

  it('reports every problem at once, with the profile name', () => {
    expect(() =>
      assertDistributionConfig({
        EAS_BUILD_PROFILE: 'preview',
        EXPO_PUBLIC_API_URL: 'http://api.test',
        EXPO_PUBLIC_DEV_LOGIN: 'true',
      }),
    ).toThrow(/"preview"[\s\S]*https:\/\/[\s\S]*EXPO_PUBLIC_DEV_LOGIN/);
  });
});

describe('createConfig', () => {
  it('fails for a distribution profile without a valid API address', () => {
    expect(() => createConfig(context, { EAS_BUILD_PROFILE: 'production' })).toThrow(
      /EXPO_PUBLIC_API_URL/,
    );
  });

  it('builds the configuration of a valid production profile', () => {
    const config = createConfig(context, production);
    expect(config.name).toBe(APP.displayName);
    expect(config.slug).toBe(APP.slug);
    expect(config.scheme).toBe(APP.urlScheme);
    expect(config.ios?.bundleIdentifier).toBe(APP.iosBundleIdentifier);
    expect(config.android?.package).toBe(APP.androidPackage);
  });

  it('names the app Uzu, with the confirmed identifiers (ADR 0041)', () => {
    expect(APP).toEqual({
      displayName: 'Uzu',
      slug: 'uzu',
      urlScheme: 'uzu',
      iosBundleIdentifier: 'com.victorsauter.uzu',
      androidPackage: 'com.victorsauter.uzu',
    });
  });

  it('keeps the scheme of the deep links of the app equal to the one of the configuration', () => {
    expect(APP_URL_SCHEME).toBe(APP.urlScheme);
  });

  it('shows the same name in the app as under the icon', () => {
    expect(fr.app.name).toBe(APP.displayName);
  });

  it('prefixes the storage keys with the scheme name, not with the former product name', () => {
    expect(SESSION_STORAGE_KEY).toBe('uzu.session');
    expect(DISCOVER_LAYOUT_STORAGE_KEY).toBe('uzu.discoverLayout');
  });

  it('writes the display name at the start of the permission texts', () => {
    const info = createConfig(context, {}).ios?.infoPlist;
    expect(info?.NSLocationWhenInUseUsageDescription).toMatch(/^Uzu utilise ta position/);
    expect(info?.NSPhotoLibraryUsageDescription).toMatch(/^Uzu accède à tes photos/);
    expect(info?.NSCameraUsageDescription).toMatch(/^Uzu utilise l’appareil photo/);
  });

  it('builds in development without any variable, keeping the previous behaviour', () => {
    const config = createConfig(context, {});
    expect(config.extra).toBeUndefined();
    expect(JSON.stringify(config.plugins)).not.toContain('react-native-maps');
    expect(config.android?.googleServicesFile).toBeUndefined();
    const plugins = (config.plugins ?? []).map((p) => (Array.isArray(p) ? p[0] : p));
    expect(plugins).not.toContain('@react-native-google-signin/google-signin');
  });

  it('uses the process environment by default', () => {
    const before = { ...process.env };
    try {
      process.env.EAS_BUILD_PROFILE = 'production';
      delete process.env.EXPO_PUBLIC_API_URL;
      expect(() => appConfig(context)).toThrow(/EXPO_PUBLIC_API_URL/);
      process.env.EXPO_PUBLIC_API_URL = 'https://api.test';
      expect(appConfig(context).name).toBe(APP.displayName);
    } finally {
      process.env = before;
    }
  });

  it('declares standard encryption only, so TestFlight stops asking the export question', () => {
    expect(createConfig(context, {}).ios?.config?.usesNonExemptEncryption).toBe(false);
  });

  it('reads the Android Maps key and the Firebase file from the environment', () => {
    const config = createConfig(context, {
      GOOGLE_MAPS_ANDROID_API_KEY: ' key-1 ',
      GOOGLE_SERVICES_JSON: '/eas/google-services.json',
    });
    expect(config.plugins).toContainEqual([
      'react-native-maps',
      { androidGoogleMapsApiKey: 'key-1' },
    ]);
    expect(config.android?.googleServicesFile).toBe('/eas/google-services.json');
  });

  it('copies the EAS project ID to extra.eas.projectId', () => {
    const config = createConfig(context, { EXPO_PUBLIC_EAS_PROJECT_ID: ' 1234 ' });
    expect(config.extra).toEqual({ eas: { projectId: '1234' } });
  });

  it('adds the Google sign-in plugin only with a valid iOS client ID', () => {
    const plugins = (env: Record<string, string>) => createConfig(context, env).plugins;
    expect(plugins({ EXPO_PUBLIC_GOOGLE_IOS_CLIENT_ID: 'nope' })).not.toContainEqual([
      '@react-native-google-signin/google-signin',
      expect.anything(),
    ]);
    expect(
      plugins({ EXPO_PUBLIC_GOOGLE_IOS_CLIENT_ID: '123-abc.apps.googleusercontent.com' }),
    ).toContainEqual([
      '@react-native-google-signin/google-signin',
      { iosUrlScheme: 'com.googleusercontent.apps.123-abc' },
    ]);
  });

  it('gives the notifications an icon and the brand colour', () => {
    const plugin = createConfig(context, {}).plugins?.find(
      (p) => Array.isArray(p) && p[0] === 'expo-notifications',
    );
    expect(plugin).toEqual([
      'expo-notifications',
      expect.objectContaining({
        icon: './assets/notification-icon.png',
        color: BRAND.primary,
      }),
    ]);
  });
});

describe('brand colours', () => {
  it('match the « Uzu » tokens (ADR 0030)', () => {
    expect(BRAND.backgroundLight).toBe(lightColors.background);
    expect(BRAND.backgroundDark).toBe(darkColors.background);
    expect(BRAND.primary).toBe(lightColors.primary);
    expect(BRAND.iconBackground).toBe(lightColors.infoSoft);
  });

  it('colour the splash screen in light and dark, and the adaptive icon in sand (ADR 0044)', () => {
    const config = createConfig(context, {});
    const splash = config.plugins?.find((p) => Array.isArray(p) && p[0] === 'expo-splash-screen');
    expect(splash).toEqual([
      'expo-splash-screen',
      expect.objectContaining({
        image: BRAND_ASSETS.splash,
        imageWidth: 200,
        backgroundColor: BRAND.backgroundLight,
        dark: { backgroundColor: BRAND.backgroundDark },
      }),
    ]);
    expect(config.android?.adaptiveIcon?.backgroundColor).toBe(BRAND.iconBackground);
    // A background image would hide the colour: the adaptive icon has none.
    expect(config.android?.adaptiveIcon?.backgroundImage).toBeUndefined();
  });
});

describe('brand assets (ADR 0044)', () => {
  /** The project has no Node types (they would change the app's globals): the file system is typed here. */
  const fs = jest.requireActual<{
    existsSync(path: string): boolean;
    readFileSync(path: string): Uint8Array;
    readFileSync(path: string, encoding: 'utf8'): string;
  }>('node:fs');
  const mobileRoot = (expect.getState().testPath ?? '').replace(
    /[\\/]src[\\/]lib[\\/]__tests__[\\/][^\\/]+$/,
    '',
  );
  const resolve = (path: string) => `${mobileRoot}/${path.replace(/^\.\//, '')}`;

  /** Width, height and colour type (2 = RGB, 6 = RGBA) read from the PNG header. */
  function pngHeader(path: string) {
    const bytes = fs.readFileSync(resolve(path));
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    expect(String.fromCharCode(view.getUint8(1), view.getUint8(2), view.getUint8(3))).toBe('PNG');
    return { width: view.getUint32(16), height: view.getUint32(20), colourType: view.getUint8(25) };
  }

  it('are the images the configuration points to', () => {
    const config = createConfig(context, {});
    expect(config.icon).toBe(BRAND_ASSETS.icon);
    expect(config.android?.adaptiveIcon?.foregroundImage).toBe(BRAND_ASSETS.androidForeground);
    expect(config.android?.adaptiveIcon?.monochromeImage).toBe(BRAND_ASSETS.androidMonochrome);
    expect(config.web?.favicon).toBe(BRAND_ASSETS.favicon);
  });

  it('give iOS a 1024 px icon without an alpha channel, as the App Store requires', () => {
    expect(pngHeader(BRAND_ASSETS.icon)).toEqual({ width: 1024, height: 1024, colourType: 2 });
  });

  it.each([BRAND_ASSETS.androidForeground, BRAND_ASSETS.androidMonochrome, BRAND_ASSETS.splash])(
    'draw %s as a transparent square layer',
    (path) => {
      expect(pngHeader(path)).toEqual({ width: 1024, height: 1024, colourType: 6 });
    },
  );

  it('give the web a 48 px favicon', () => {
    expect(pngHeader(BRAND_ASSETS.favicon)).toMatchObject({ width: 48, height: 48 });
  });

  it('keep the vector source with the parts the export script uses', () => {
    const source = resolve('assets/brand/uzu-head.svg');
    expect(fs.existsSync(source)).toBe(true);
    const svg = fs.readFileSync(source, 'utf8');
    for (const id of ['ears', 'head', 'markings', 'features']) {
      expect(svg).toContain(`id="${id}"`);
    }
  });
});
