import type {
  AuthResponse,
  Dog,
  Membership,
  MyMembership,
  OrganizationDetail,
  OrganizationSummary,
  Participant,
  SessionUser,
  WalkDetail,
  WalkSearchResult,
} from '@/api/types';

export const testUser: SessionUser = {
  id: '6f1d3c1e-0000-4000-8000-000000000001',
  displayName: 'Léa Martin',
  bio: '',
  photoUrl: null,
  email: 'lea@example.test',
  status: 'Active',
  isAdmin: false,
  createdAt: '2026-11-01T10:00:00Z',
};

let counter = 0;

/** A sign-in/refresh response whose access token expires `expiresInMs` after `now`. */
export function authResponse(
  options: { expiresInMs?: number; now?: number; user?: Partial<SessionUser> } = {},
): AuthResponse {
  counter += 1;
  const now = options.now ?? Date.now();
  return {
    accessToken: `access-${counter}`,
    accessTokenExpiresAt: new Date(now + (options.expiresInMs ?? 15 * 60_000)).toISOString(),
    refreshToken: `refresh-${counter}`,
    user: { ...testUser, ...options.user },
  };
}

export function jsonResponse(body: unknown, status = 200, contentType = 'application/json') {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': contentType } });
}

export function problemResponse(status: number, code?: string, extra: object = {}) {
  return jsonResponse(
    { type: 'about:blank', title: 'Problem', status, ...(code ? { code } : {}), ...extra },
    status,
    'application/problem+json',
  );
}

export const testDog: Dog = {
  id: 'd0d0c1e0-0000-4000-8000-000000000001',
  members: [{ user: testUser, role: 'Owner' }],
  name: 'Biscotte',
  photoUrl: null,
  size: 'Medium',
  birthDate: '2022-03-15',
  breed: 'Border collie',
  sociability: 'Easygoing',
  reactivity: 'Mild',
  leashHabit: 'LongLine',
  description: 'Adore les balades en forêt.',
  createdAt: '2026-11-02T10:00:00Z',
};

const inDays = (days: number, hour = 10) => {
  const date = new Date();
  date.setDate(date.getDate() + days);
  date.setHours(hour, 0, 0, 0);
  return date.toISOString();
};

export const otherUser = {
  id: '6f1d3c1e-0000-4000-8000-000000000002',
  displayName: 'Hugo Bernard',
  photoUrl: null,
};

export const otherDog: Dog = {
  ...testDog,
  id: 'd0d0c1e0-0000-4000-8000-000000000002',
  members: [{ user: otherUser, role: 'Owner' }],
  name: 'Pixel',
  size: 'Small',
};

/** A search result, organised by someone else, in two days. */
export function walkResult(overrides: Partial<WalkSearchResult> = {}): WalkSearchResult {
  return {
    id: 'a1a1a1a1-0000-4000-8000-000000000001',
    title: 'Tour du lac',
    startsAt: inDays(2),
    durationMinutes: 90,
    location: { latitude: 47.5324, longitude: 7.1858 },
    locationLabel: 'Entrée du parc',
    distanceMeters: 1250,
    maxDogs: 6,
    acceptedDogsCount: 2,
    allowedSizes: ['Small', 'Medium'],
    leashRule: 'LeashRequired',
    access: 'Open',
    status: 'Scheduled',
    organizer: otherUser,
    isProfessional: false,
    organization: null,
    repeatsWeekly: false,
    hasBlockedParticipant: false,
    ...overrides,
  };
}

/** A walk detail, organised by someone else (not me), in two days. */
export function walkDetail(overrides: Partial<WalkDetail> = {}): WalkDetail {
  const { distanceMeters: _distance, ...base } = walkResult();
  return {
    ...base,
    description: 'Une boucle tranquille.',
    rules: 'Ramasser derrière son chien.',
    createdAt: '2026-11-10T10:00:00Z',
    isOrganizer: false,
    participants: [],
    myRegistrations: [],
    myDogsRegistrations: [],
    pendingRegistrations: [],
    ...overrides,
  };
}

export function participant(dog: Dog, registrationId: string, owner = otherUser): Participant {
  return { registrationId, dog, owner };
}

// ---- Organisations (lot 4)

export const orgId = 'c0c0c0c0-0000-4000-8000-000000000001';

export function organizationSummary(
  overrides: Partial<OrganizationSummary> = {},
): OrganizationSummary {
  return {
    id: orgId,
    name: 'Club canin du lac',
    description: 'Éducation positive, cours collectifs.',
    city: 'Annecy',
    status: 'Approved',
    memberCount: 12,
    createdAt: '2026-11-01T10:00:00Z',
    ...overrides,
  };
}

/** The detail of an organisation, seen by someone who is not a member. */
export function organizationDetail(
  overrides: Partial<OrganizationDetail> = {},
): OrganizationDetail {
  const { id, name, description, city, status, memberCount, createdAt } = organizationSummary();
  return {
    id,
    name,
    description,
    city,
    status,
    memberCount,
    createdAt,
    educators: [{ user: otherUser, role: 'Owner' }],
    myMembership: null,
    upcomingWalks: [],
    ...overrides,
  };
}

export function myMembership(
  role: MyMembership['role'] = 'Member',
  status: MyMembership['status'] = 'Accepted',
): MyMembership {
  return { id: 'e0e0e0e0-0000-4000-8000-0000000000aa', role, status };
}

export function membership(overrides: Partial<Membership> = {}): Membership {
  return {
    id: 'e0e0e0e0-0000-4000-8000-000000000001',
    organizationId: orgId,
    user: otherUser,
    role: 'Member',
    status: 'Accepted',
    createdAt: '2026-11-05T10:00:00Z',
    ...overrides,
  };
}
