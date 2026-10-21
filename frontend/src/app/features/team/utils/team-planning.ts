import { DayOfWeekName, GridDay } from '../models/team-planning.model';
import { TIME_OPTIONS, formatExceptionDate, toMinutes } from './skills-availability';
import { formatWindowTime } from './team-format';

export const DAY_KEYS: readonly DayOfWeekName[] = [
  'monday',
  'tuesday',
  'wednesday',
  'thursday',
  'friday',
  'saturday',
  'sunday',
];

export const DAY_LABELS: Readonly<Record<DayOfWeekName, string>> = {
  monday: 'Monday',
  tuesday: 'Tuesday',
  wednesday: 'Wednesday',
  thursday: 'Thursday',
  friday: 'Friday',
  saturday: 'Saturday',
  sunday: 'Sunday',
};

export const STALE_MESSAGE =
  'This technician was updated by someone else. Reload to see the latest changes.';
export const UNAVAILABLE_TECHNICIAN_MESSAGE = "This technician profile isn't available.";
export const END_BEFORE_START_MESSAGE = 'End time must be after start time.';

const asDate = (value: string): Date => {
  const [year, month, day] = value.split('-').map(Number);
  return new Date(Date.UTC(year, month - 1, day));
};

const format = (value: string, options: Intl.DateTimeFormatOptions): string =>
  new Intl.DateTimeFormat('en-US', { ...options, timeZone: 'UTC' }).format(asDate(value));

/** `yyyy-MM-dd` shifted by whole days (calendar arithmetic, no time zone). */
export function addDays(value: string, days: number): string {
  const date = asDate(value);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
}

/** BR-02: "Oct 19 – 25, 2026", or "Oct 27 – Nov 2, 2026" across months or years. */
export function weekRangeLabel(weekStart: string): string {
  const end = addDays(weekStart, 6);
  const sameMonth = weekStart.slice(0, 7) === end.slice(0, 7);
  const from = format(weekStart, { month: 'short', day: 'numeric' });
  const to = sameMonth
    ? `${format(end, { day: 'numeric' })}, ${end.slice(0, 4)}`
    : format(end, { month: 'short', day: 'numeric', year: 'numeric' });
  return weekStart.slice(0, 4) !== end.slice(0, 4)
    ? `${format(weekStart, { month: 'short', day: 'numeric', year: 'numeric' })} – ${to}`
    : `${from} – ${to}`;
}

/** BR-02: "Mon 19". */
export function dayHeader(value: string): string {
  return `${format(value, { weekday: 'short' })} ${format(value, { day: 'numeric' })}`;
}

export function weekDates(weekStart: string): readonly string[] {
  return DAY_KEYS.map((_, index) => addDays(weekStart, index));
}

/** BR-05: whole or one-decimal hours, "45 h" or "37.5 h". */
export function weeklyHours(minutes: number): string {
  return `${Math.round(minutes / 6) / 10} h`;
}

/** BR-04: cells carry text, never color alone. */
export function cellText(day: GridDay): string {
  if (day.state === 'time_off') {
    return 'Time off';
  }
  if (day.state === 'available' && day.start !== null && day.end !== null) {
    return `${formatWindowTime(day.start)} – ${formatWindowTime(day.end)}`;
  }
  return 'Unavailable';
}

export function initials(firstName: string, lastName: string): string {
  return `${firstName.charAt(0)}${lastName.charAt(0)}`.toUpperCase();
}

/** BR-13: "MMM d, yyyy – MMM d, yyyy", a single date when both ends match. */
export function timeOffRange(startDate: string, endDate: string): string {
  return startDate === endDate
    ? formatExceptionDate(startDate)
    : `${formatExceptionDate(startDate)} – ${formatExceptionDate(endDate)}`;
}

export interface TimeChoice {
  readonly value: string;
  readonly label: string;
}

/** BR-08: 30-minute steps; a loaded off-step value is added and preserved. */
export function timeChoices(current: string | null): TimeChoice[] {
  if (current === null || TIME_OPTIONS.some((option) => option.value === current)) {
    return [...TIME_OPTIONS];
  }
  return [...TIME_OPTIONS, { value: current, label: formatWindowTime(current) }].sort(
    (a, b) => toMinutes(a.value) - toMinutes(b.value),
  );
}

export const hm = (value: string | null): string | null =>
  value === null ? null : value.slice(0, 5);

/** First message of a `400` field key, matched case-insensitively (`days.monday.end`). */
export function fieldMessages(
  fieldErrors: Readonly<Record<string, readonly string[]>>,
): Readonly<Record<string, string>> {
  const result: Record<string, string> = {};
  for (const [key, messages] of Object.entries(fieldErrors)) {
    if (messages.length > 0) {
      result[key.toLowerCase()] = messages[0];
    }
  }
  return result;
}
