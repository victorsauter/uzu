import type { Href } from 'expo-router';

import { APP_URL_SCHEME } from '@/lib/appLinks';

/** Types of notification the API sends (api/README.md, « Notifications »). */
export const notificationTypes = [
  'registration_created',
  'registration_requested',
  'registration_accepted',
  'registration_declined',
  // Another owner of my dog cancelled its registration (ADR 0042): opens the walk.
  'registration_cancelled_by_owner',
  'walk_updated',
  'walk_cancelled',
  'message_posted',
  'walk_reminder',
  'series_walk_created',
  'membership_requested',
  'membership_accepted',
  'organization_approved',
  // Administrators only (ADR 0036): shown by the system, nothing to open (no administration screen).
  'report_created',
] as const;

export type NotificationType = (typeof notificationTypes)[number];

/** Types shown by the system and never followed: a tap leads nowhere, whatever link they carry. */
const informationOnlyTypes: readonly NotificationType[] = ['report_created'];

/** Types whose link points to an organisation (payload `organizationId`), not to a walk. */
const organizationTypes: readonly NotificationType[] = [
  'membership_requested',
  'membership_accepted',
  'organization_approved',
];

/** The only links a notification may carry: `uzu://walks/<id>` and `uzu://organizations/<id>` (the scheme is `APP_URL_SCHEME`). */
const WALK_URL = new RegExp(`^${APP_URL_SCHEME}:\\/\\/walks\\/([A-Za-z0-9-]{1,64})\\/?$`);
const ORGANIZATION_URL = new RegExp(
  `^${APP_URL_SCHEME}:\\/\\/organizations\\/([A-Za-z0-9-]{1,64})\\/?$`,
);

export interface NotificationTarget {
  /** Set for a walk link. */
  walkId?: string;
  /** Set for an organisation link. */
  organizationId?: string;
  type: NotificationType | null;
  href: Href;
}

/**
 * Where tapping a notification leads, or `null` to ignore it (a `report_created`
 * has no link: it is only shown, and a tap on it changes nothing). Only links that
 * match the app's own routes are followed (anything else, other schemes and
 * hosts included, is dropped), and the type must agree with the kind of link.
 * Walks: a `message_posted` opens the thread, every other type the walk
 * detail. Organisations: a `membership_requested` opens the members screen
 * (that is where the request is answered), the other types the organisation.
 */
export function targetForNotification(data: unknown): NotificationTarget | null {
  if (typeof data !== 'object' || data === null) return null;
  const { url, type, walkId, organizationId } = data as Record<string, unknown>;
  if (typeof url !== 'string') return null;
  const link = url.trim();
  const known = notificationTypes.find((value) => value === type) ?? null;
  if (known !== null && informationOnlyTypes.includes(known)) return null;
  const isOrganizationType = known !== null && organizationTypes.includes(known);

  const walk = WALK_URL.exec(link)?.[1];
  if (walk) {
    if (isOrganizationType || (walkId !== undefined && walkId !== walk)) return null;
    const href: Href =
      known === 'message_posted'
        ? { pathname: '/walks/[id]/messages', params: { id: walk } }
        : { pathname: '/walks/[id]', params: { id: walk } };
    return { walkId: walk, type: known, href };
  }

  const organization = ORGANIZATION_URL.exec(link)?.[1];
  if (organization) {
    if (
      (known !== null && !isOrganizationType) ||
      (organizationId !== undefined && organizationId !== organization)
    ) {
      return null;
    }
    const href: Href =
      known === 'membership_requested'
        ? { pathname: '/organizations/[id]/members', params: { id: organization } }
        : { pathname: '/organizations/[id]', params: { id: organization } };
    return { organizationId: organization, type: known, href };
  }
  return null;
}
