import {
  AvailabilityDay,
  NextAvailable,
  ProfileStatus,
  SkillHealth,
  TodayStatus,
  WorkloadState,
} from '../models/team.model';

import { localized, localizedList, t } from '../../../core/i18n/translate';
import { activeIntlLocale } from '../../../core/i18n/active-locale';
type TagSeverity = 'success' | 'info' | 'warn' | 'secondary';

export const STATUS_LABELS: Readonly<Record<TodayStatus, string>> = localized(() => ({
  on_job: t('team.teamFormat.onJob'),
  available: t('team.teamFormat.available'),
  break: t('team.teamFormat.break'),
  time_off: t('team.teamFormat.timeOff'),
  off: t('team.teamFormat.off'),
  inactive: t('common.inactive'),
  suspended: t('team.teamFormat.suspended'),
}));

const STATUS_SEVERITIES: Readonly<Record<TodayStatus, TagSeverity>> = {
  on_job: 'info',
  available: 'success',
  break: 'warn',
  time_off: 'warn',
  off: 'secondary',
  inactive: 'secondary',
  suspended: 'secondary',
};

export function statusSeverity(status: TodayStatus): TagSeverity {
  return STATUS_SEVERITIES[status];
}

export const PROFILE_STATUS_LABELS: Readonly<Record<ProfileStatus, string>> = localized(() => ({
  active: t('common.active'),
  inactive: t('common.inactive'),
  suspended: t('team.teamFormat.suspended'),
}));

export function profileStatusSeverity(status: ProfileStatus): TagSeverity {
  return status === 'active' ? 'success' : 'secondary';
}

export const HEALTH_LABELS: Readonly<Record<SkillHealth, string>> = localized(() => ({
  healthy: t('team.teamFormat.healthy'),
  watch: t('team.teamFormat.watch'),
  low: t('team.teamFormat.lowCoverage'),
}));

export const HEALTH_SEVERITIES: Readonly<Record<SkillHealth, TagSeverity>> = {
  healthy: 'success',
  watch: 'warn',
  low: 'warn',
};

/** BR-25. */
const PROFICIENCY_LABELS: Readonly<Record<number, string>> = localized(() => ({
  1: t('team.teamFormat.beginner'),
  2: t('team.teamFormat.basic'),
  3: t('team.teamFormat.intermediate'),
  4: t('team.teamFormat.advanced'),
  5: t('team.teamFormat.expert'),
}));

export function proficiencyLabel(level: number | null | undefined): string | null {
  return level === null || level === undefined ? null : (PROFICIENCY_LABELS[level] ?? null);
}

export interface WorkloadView {
  readonly text: string;
  /** Bar fill 0–100, or null when no bar is drawn. */
  readonly fill: number | null;
  readonly over: boolean;
}

/** BR-06: percent with a bar (red above 100), "No availability", or "—". */
export function workloadView(
  state: WorkloadState,
  percent: number | null | undefined,
): WorkloadView {
  if (state === 'no_availability') {
    return { text: t('team.teamFormat.noAvailability'), fill: null, over: true };
  }
  if (state === 'percent' && typeof percent === 'number') {
    return { text: `${percent}%`, fill: Math.min(100, Math.max(0, percent)), over: percent > 100 };
  }
  return { text: '—', fill: null, over: false };
}

function parts(iso: string, timeZone: string, options: Intl.DateTimeFormatOptions): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return '—';
  }
  const format = (zone: string | undefined) =>
    new Intl.DateTimeFormat(activeIntlLocale(), { ...options, timeZone: zone }).format(date);
  try {
    return format(timeZone);
  } catch {
    return format('UTC');
  }
}

/** BR-03: "h:mm a" in the returned timezone. */
export function formatClock(iso: string, timeZone: string): string {
  return parts(iso, timeZone, { hour: 'numeric', minute: '2-digit', hour12: true }).replace(
    /\s/g,
    ' ',
  );
}

/** BR-03: "EEE, MMM d" in the returned timezone. */
export function formatDay(iso: string, timeZone: string): string {
  return parts(iso, timeZone, { weekday: 'short', month: 'short', day: 'numeric' });
}

/** BR-07: "Now", a time today, a dated future window, or "—". */
export function nextAvailableText(
  next: NextAvailable,
  timeZone: string,
  now: Date = new Date(),
): string {
  if (next.kind === 'now') {
    return t('team.teamFormat.now');
  }
  if (next.kind !== 'time' || !next.at) {
    return '—';
  }
  const dayKey = (iso: string) => parts(iso, timeZone, { dateStyle: 'short' });
  return dayKey(next.at) === dayKey(now.toISOString())
    ? formatClock(next.at, timeZone)
    : formatDay(next.at, timeZone);
}

export interface AvailabilityLine {
  readonly label: string;
  readonly value: string;
}

const DAY_NAMES = localizedList(() => [
  t('team.teamFormat.sunday'),
  t('team.teamFormat.monday'),
  t('team.teamFormat.tuesday'),
  t('team.teamFormat.wednesday'),
  t('team.teamFormat.thursday'),
  t('team.teamFormat.friday'),
  t('team.teamFormat.saturday'),
]);
/** Monday first, Sunday last. */
const WEEK_ORDER = [1, 2, 3, 4, 5, 6, 0];

/** "08:00" or "08:00:00" to "8:00 AM". */
export function formatWindowTime(value: string): string {
  const [hours, minutes] = value.split(':').map(Number);
  if (!Number.isFinite(hours) || !Number.isFinite(minutes)) {
    return value;
  }
  const suffix = hours >= 12 ? 'PM' : 'AM';
  return `${hours % 12 === 0 ? 12 : hours % 12}:${String(minutes).padStart(2, '0')} ${suffix}`;
}

/** BR-13: groups consecutive days with identical windows; empty when no day has a window. */
export function groupAvailability(days: readonly AvailabilityDay[]): readonly AvailabilityLine[] {
  const byDay = new Map<number, string>();
  for (const day of days) {
    byDay.set(
      day.dayOfWeek,
      [...day.windows]
        .sort((a, b) => a.start.localeCompare(b.start))
        .map((window) => `${formatWindowTime(window.start)} – ${formatWindowTime(window.end)}`)
        .join(', '),
    );
  }
  if ([...byDay.values()].every((value) => value === '')) {
    return [];
  }
  const valueOf = (day: number): string => byDay.get(day) ?? '';
  const lines: AvailabilityLine[] = [];
  let index = 0;
  while (index < WEEK_ORDER.length) {
    const day = WEEK_ORDER[index];
    const value = valueOf(day);
    if (value === '') {
      if (day === 6 && valueOf(0) === '') {
        lines.push({ label: t('team.teamFormat.weekends'), value: t('team.teamFormat.off') });
        break;
      }
      lines.push({ label: DAY_NAMES[day], value: t('team.teamFormat.off') });
      index += 1;
      continue;
    }
    let end = index;
    while (end + 1 < WEEK_ORDER.length && valueOf(WEEK_ORDER[end + 1]) === value) {
      end += 1;
    }
    const from = DAY_NAMES[day];
    const to = DAY_NAMES[WEEK_ORDER[end]];
    lines.push({ label: end > index ? `${from} – ${to}` : from, value });
    index = end + 1;
  }
  return lines;
}
