import type { EnumName, EnumValue } from '@/api/enums';

import { fr } from './fr';

export type { ApiErrorCode, Strings } from './fr';

/**
 * Typed accessor to the French texts: `t.tabs.discover`, `t.auth.signOut`…
 * The app is French-only (V1); keep this indirection so a locale can be added later.
 */
export const t = fr;

/** A vowel or a mute-looking start: « de » becomes « d’ » before it (Oscar, Élodie, Antoine). */
const ELIDING_START = /^[aeiouàâäéèêëîïôöùûüœæ]/i;

/**
 * Fills `{name}` placeholders: `format(t.profile.greeting, { name: 'Léa' })`.
 * French elision: « de {name} » becomes « d’Oscar » when the value starts with a vowel
 * (`h` and `y` are left alone: « de Hugo », « de Yannick »).
 */
export function format(template: string, params: Record<string, string | number>): string {
  return template.replace(/(\b[Dd]e )?\{(\w+)\}/g, (match, de: string | undefined, key: string) => {
    if (!(key in params)) return match;
    const value = String(params[key]);
    if (!de) return value;
    return ELIDING_START.test(value) ? `${de[0]}’${value}` : `${de}${value}`;
  });
}

/**
 * Picks the singular or plural form of a `one|many` template and fills
 * `{count}` (French: 0 and 1 take the singular): `plural(t.walk.registerButton, 2)`.
 */
export function plural(
  template: string,
  count: number,
  params: Record<string, string | number> = {},
): string {
  const [one, many = one] = template.split('|') as [string, string?];
  return format(Math.abs(count) < 2 ? one : many, { count, ...params });
}

/** French label of an API enum value: `enumLabel('DogSize', 'Small')` → « Petit ». */
export function enumLabel<E extends EnumName>(name: E, value: EnumValue<E>): string {
  return (fr.enums[name] as Record<string, string>)[value] ?? value;
}
