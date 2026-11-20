import { format, t } from '@/strings';

/**
 * Dates and times of walks, in French and in the device's time zone (the API
 * sends UTC instants; `Intl` converts them with the device settings).
 */

const LOCALE = 'fr-FR';

const timeFormat = new Intl.DateTimeFormat(LOCALE, { hour: '2-digit', minute: '2-digit' });
const dayFormat = new Intl.DateTimeFormat(LOCALE, {
  weekday: 'long',
  day: 'numeric',
  month: 'long',
});
const dayWithYearFormat = new Intl.DateTimeFormat(LOCALE, {
  weekday: 'long',
  day: 'numeric',
  month: 'long',
  year: 'numeric',
});
const shortDayFormat = new Intl.DateTimeFormat(LOCALE, {
  weekday: 'short',
  day: 'numeric',
  month: 'short',
});

const sameDay = (a: Date, b: Date) =>
  a.getFullYear() === b.getFullYear() &&
  a.getMonth() === b.getMonth() &&
  a.getDate() === b.getDate();

function addDays(date: Date, days: number): Date {
  const copy = new Date(date);
  copy.setDate(copy.getDate() + days);
  return copy;
}

/** « 09:30 » */
export function formatTime(date: Date | string): string {
  return timeFormat.format(new Date(date));
}

/**
 * « Aujourd’hui », « Demain », « samedi 22 novembre », or with the year when it
 * is not the current one (« lundi 4 janvier 2027 »).
 */
export function formatDay(date: Date | string, now: Date = new Date()): string {
  const value = new Date(date);
  if (sameDay(value, now)) return t.dates.today;
  if (sameDay(value, addDays(now, 1))) return t.dates.tomorrow;
  if (sameDay(value, addDays(now, -1))) return t.dates.yesterday;
  return (value.getFullYear() === now.getFullYear() ? dayFormat : dayWithYearFormat).format(value);
}

/** « Demain à 09:30 » */
export function formatWalkStart(date: Date | string, now: Date = new Date()): string {
  return format(t.walk.when, { day: formatDay(date, now), time: formatTime(date) });
}

/** « sam. 22 nov. » (compact, for form buttons). */
export function formatShortDay(date: Date | string): string {
  return shortDayFormat.format(new Date(date));
}

const dateFormat = new Intl.DateTimeFormat(LOCALE, { day: 'numeric', month: 'long' });
const dateWithYearFormat = new Intl.DateTimeFormat(LOCALE, {
  day: 'numeric',
  month: 'long',
  year: 'numeric',
});

/** « 27 novembre », with the year when it is not the current one (« 4 janvier 2027 »). */
export function formatDate(date: Date | string, now: Date = new Date()): string {
  const value = new Date(date);
  return (value.getFullYear() === now.getFullYear() ? dateFormat : dateWithYearFormat).format(
    value,
  );
}

/** « 45 min », « 1 h », « 1 h 30 » */
export function formatDuration(minutes: number): string {
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  if (hours === 0) return format(t.dates.minutes, { minutes: rest });
  if (rest === 0) return format(t.dates.hours, { hours });
  return format(t.dates.hoursMinutes, { hours, minutes: String(rest).padStart(2, '0') });
}

/** End of the walk (start + duration). */
export function walkEnd(walk: { startsAt: string; durationMinutes: number }): Date {
  return new Date(new Date(walk.startsAt).getTime() + walk.durationMinutes * 60_000);
}

export type WalkTiming = 'upcoming' | 'started' | 'past';

/** Where `now` stands relative to the walk: registrations only happen while `upcoming`. */
export function walkTiming(
  walk: { startsAt: string; durationMinutes: number },
  now: Date = new Date(),
): WalkTiming {
  if (now.getTime() < new Date(walk.startsAt).getTime()) return 'upcoming';
  return now.getTime() < walkEnd(walk).getTime() ? 'started' : 'past';
}

/** Last instant of the local day of `now` (for the « Aujourd’hui » filter). */
export function endOfDay(now: Date = new Date()): Date {
  const end = new Date(now);
  end.setHours(23, 59, 59, 999);
  return end;
}

/** Default start of a new walk: tomorrow at 10:00, local time. */
export function defaultWalkStart(now: Date = new Date()): Date {
  const start = addDays(now, 1);
  start.setHours(10, 0, 0, 0);
  return start;
}

/**
 * An instant as the API wants it: ISO 8601 in UTC with its explicit `Z`
 * (`2026-11-19T09:00:00.000Z`). The API refuses a date without an offset, so
 * every date or time sent in a body or a query goes through here, never through
 * a formatter that would drop the offset (`toLocaleString`, a `YYYY-MM-DD` slice).
 */
export function toApiDateTime(date: Date): string {
  return date.toISOString();
}

/** How far ahead a walk may start: one year (`WithinYears(1)` of the API). */
export const WALK_START_MAX_YEARS = 1;

/** Latest instant a walk may start at, from `now` (the API adds one year to the server clock). */
export function latestWalkStart(now: Date = new Date()): Date {
  const latest = new Date(now);
  latest.setFullYear(latest.getFullYear() + WALK_START_MAX_YEARS);
  return latest;
}
