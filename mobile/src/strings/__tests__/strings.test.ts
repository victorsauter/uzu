import { enumValues, type EnumName } from '@/api/enums';

import { enumLabel, format, t } from '..';

// The project has no Node type definitions: only what this test uses is declared.
declare const __dirname: string;
/* eslint-disable @typescript-eslint/no-require-imports */
const fs = require('node:fs') as {
  existsSync(path: string): boolean;
  readFileSync(path: string, encoding: 'utf8'): string;
};
const path = require('node:path') as { join(...parts: string[]): string };
/* eslint-enable @typescript-eslint/no-require-imports */

/** Labels of spec §2, copied from the specification table. */
const specLabels: Record<EnumName, string[]> = {
  DogSize: ['Petit', 'Moyen', 'Grand'],
  Sociability: ['À l’aise', 'Sélectif', 'Réservé'],
  Reactivity: ['Aucune', 'Légère', 'Marquée'],
  LeashHabit: ['En laisse', 'En longe', 'En liberté'],
  LeashRule: ['Laisse obligatoire', 'Liberté possible'],
  WalkAccess: ['Ouverte', 'Sur approbation'],
  WalkStatus: ['Prévue', 'Annulée'],
  RegistrationStatus: ['En attente', 'Acceptée', 'Refusée', 'Annulée'],
  OrganizationStatus: ['En attente', 'Validée', 'Refusée', 'Fermée'],
  DogMemberRole: ['Propriétaire'],
  MembershipRole: ['Responsable', 'Éducateur', 'Membre'],
  MembershipStatus: ['En attente', 'Accepté', 'Refusé'],
  ReportType: [
    'Incident sans blessure',
    'Altercation',
    'Morsure ou blessure',
    'Comportement d’un humain',
  ],
  ReportStatus: ['Ouvert', 'Examiné', 'Clos'],
  UserStatus: ['Actif', 'Suspendu'],
};

// The spec writes apostrophes as ' ; the app uses the typographic ’.
const normalize = (label: string) => label.replace(/'/g, '’');

describe('enum labels', () => {
  it('covers every enumeration of the specification', () => {
    expect(Object.keys(t.enums).sort()).toEqual(Object.keys(enumValues).sort());
  });

  it.each(Object.keys(enumValues) as EnumName[])('%s: every value has its French label', (name) => {
    const labels = enumValues[name].map((value) => enumLabel(name, value as never));
    expect(labels).toEqual(specLabels[name].map(normalize));
    expect(Object.keys(t.enums[name])).toEqual([...enumValues[name]]);
  });
});

describe('error messages', () => {
  it.each([
    'walk.full',
    'walk.size_not_allowed',
    'registration.duplicate',
    'walk.not_open',
    'walk.members_only',
    'walk.organization_forbidden',
    'organization.not_found',
    'organization.forbidden',
    'organization.closed',
    'organization.not_approved',
    'organization.too_many_pending',
    'membership.duplicate',
    'membership.not_found',
    'membership.not_pending',
    'membership.owner_role',
    'membership.not_accepted',
    'membership.sole_owner',
    'block.self',
    'report.self',
    'report.not_found',
    'admin.forbidden',
    'admin.self_suspension',
    'walk.capacity_below_registered',
    'account.suspended',
    'validation.failed',
    'photo.too_large',
    'photo.unsupported_type',
    'photo.missing',
    'dog.not_found',
    'dog.forbidden',
    'user.not_found',
    'auth.invalid_token',
    'generic',
    'network',
  ])('has a French message for %s', (code) => {
    expect((t.errors as Record<string, string>)[code]).toMatch(/\S/);
  });
});

describe('refusals that must not reveal anything', () => {
  it('walk.not_allowed is gone: a blocked person gets the answer of a full walk (ADR 0037)', () => {
    expect(t.errors).not.toHaveProperty('walk.not_allowed');
  });

  it('the message of walk.full does not suggest a block', () => {
    expect(t.errors['walk.full']).not.toMatch(/bloqu|interdit|banni|refus/i);
  });
});

describe('format', () => {
  it('fills placeholders and keeps unknown ones', () => {
    expect(format('Bonjour {name} ({missing})', { name: 'Léa' })).toBe('Bonjour Léa ({missing})');
  });

  it('elides « de » before a vowel, not before a consonant, h or y', () => {
    expect(format('Ne plus m’occuper de {name}', { name: 'Oscar' })).toBe(
      'Ne plus m’occuper d’Oscar',
    );
    expect(format('Les chiens de {name}', { name: 'Élodie' })).toBe('Les chiens d’Élodie');
    expect(format('De {name} à toi', { name: 'Antoine' })).toBe('D’Antoine à toi');
    expect(format('Les chiens de {name}', { name: 'Filou' })).toBe('Les chiens de Filou');
    expect(format('Les chiens de {name}', { name: 'Hugo' })).toBe('Les chiens de Hugo');
    expect(format('Les chiens de {name}', { name: 'Yannick' })).toBe('Les chiens de Yannick');
    expect(format('Prise de {count} places', { count: 8 })).toBe('Prise de 8 places');
    expect(format('Mode {name}', { name: 'Oscar' })).toBe('Mode Oscar');
  });
});

describe('API error codes of walks and registrations', () => {
  it.each([
    'walk.not_found',
    'walk.forbidden',
    'walk.not_open',
    'walk.full',
    'walk.size_not_allowed',
    'walk.capacity_below_registered',
    'walk.size_in_use',
    'walk.members_only',
    'registration.not_found',
    'registration.forbidden',
    'registration.duplicate',
    'registration.not_active',
  ])('%s has a French message', (code) => {
    expect((t.errors as Record<string, string>)[code]).toEqual(expect.any(String));
  });
});

describe('codes of the API (api/src/Dogo.Api/Common/ErrorCodes.cs)', () => {
  /**
   * Codes the app needs no message of their own for: the HTTP status decides
   * (`t.errors.notFound`, `server`, `generic`…) or the user cannot meet them (405).
   */
  const decidedByStatus = ['error', 'not_found', 'method_not_allowed', 'server.error'];
  const codesFile = path.join(
    __dirname,
    '..',
    '..',
    '..',
    '..',
    'api',
    'src',
    'Dogo.Api',
    'Common',
    'ErrorCodes.cs',
  );
  // The whole repository is checked out in CI and by agents; a standalone copy of `mobile/` skips these tests.
  const apiCodes = fs.existsSync(codesFile)
    ? [...fs.readFileSync(codesFile, 'utf8').matchAll(/const string \w+ = "([^"]+)"/g)].map(
        (match) => match[1]!,
      )
    : [];

  it('reads the list of codes', () => {
    if (fs.existsSync(codesFile)) expect(apiCodes.length).toBeGreaterThan(40);
  });

  it.each(apiCodes.filter((code) => !decidedByStatus.includes(code)))(
    '%s has a French message',
    (code) => {
      expect((t.errors as Record<string, string>)[code]).toMatch(/\S/);
    },
  );

  it('keeps no message for a code the API no longer sends', () => {
    if (apiCodes.length === 0) return;
    const known = Object.keys(t.errors).filter((key) => key.includes('.'));
    expect(known.filter((code) => !apiCodes.includes(code))).toEqual([]);
  });
});

describe('new codes of the review', () => {
  it.each([
    ['dog.size_in_use', /inscrit/],
    ['dog.limit_reached', /20 chiens/],
    ['request.too_large', /volumineux/],
    ['rate_limit.exceeded', /Trop de demandes/],
  ])('%s is explained in plain words', (code, pattern) => {
    expect((t.errors as Record<string, string>)[code]).toMatch(pattern);
  });

  it('walk.not_open covers a walk whose organisation is no longer validated', () => {
    expect(t.errors['walk.not_open']).toMatch(/organisation/);
  });
});
