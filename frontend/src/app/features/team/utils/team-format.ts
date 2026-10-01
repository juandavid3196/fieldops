import {
  AvailabilityDay,
  NextAvailable,
  ProfileStatus,
  SkillHealth,
  TodayStatus,
  WorkloadState,
} from '../models/team.model';

type TagSeverity = 'success' | 'info' | 'warn' | 'secondary';

export const STATUS_LABELS: Readonly<Record<TodayStatus, string>> = {
  on_job: 'On job',
  available: 'Available',
  break: 'Break',
  time_off: 'Time off',
  off: 'Off',
  inactive: 'Inactive',
  suspended: 'Suspended',
};

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

export const PROFILE_STATUS_LABELS: Readonly<Record<ProfileStatus, string>> = {
  active: 'Active',
  inactive: 'Inactive',
  suspended: 'Suspended',
};

export function profileStatusSeverity(status: ProfileStatus): TagSeverity {
  return status === 'active' ? 'success' : 'secondary';
}

export const HEALTH_LABELS: Readonly<Record<SkillHealth, string>> = {
  healthy: 'Healthy',
  watch: 'Watch',
  low: 'Low coverage',
};

export const HEALTH_SEVERITIES: Readonly<Record<SkillHealth, TagSeverity>> = {
  healthy: 'success',
  watch: 'warn',
  low: 'warn',
};

/** BR-25. */
const PROFICIENCY_LABELS: Readonly<Record<number, string>> = {
  1: 'Beginner',
  2: 'Basic',
  3: 'Intermediate',
  4: 'Advanced',
  5: 'Expert',
};

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
    return { text: 'No availability', fill: null, over: true };
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
    new Intl.DateTimeFormat('en-US', { ...options, timeZone: zone }).format(date);
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
    return 'Now';
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

const DAY_NAMES = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
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
        lines.push({ label: 'Weekends', value: 'Off' });
        break;
      }
      lines.push({ label: DAY_NAMES[day], value: 'Off' });
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
