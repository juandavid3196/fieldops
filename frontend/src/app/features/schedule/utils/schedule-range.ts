import {
  CalendarRange,
  CalendarTechnician,
  CalendarVisit,
  LoadState,
  ScheduleView,
} from '../models/schedule.model';

/** BR-04 default Day axis when no displayed technician has availability. */
export const DEFAULT_AXIS_START_HOUR = 8;
export const DEFAULT_AXIS_END_HOUR = 17;

export type LoadTone = 'teal' | 'amber' | 'red' | 'none';

export interface LoadInfo {
  readonly text: string;
  readonly tone: LoadTone;
  readonly percent: number;
}

export interface HourAxis {
  readonly startHour: number;
  readonly endHour: number;
  readonly hours: readonly number[];
}

export interface BlockGeometry {
  /** Percent from the inline start of the axis. */
  readonly left: number;
  readonly width: number;
}

const DATE_PATTERN = /^\d{4}-\d{2}-\d{2}$/;
const MS_PER_DAY = 86_400_000;

export function isValidDate(value: string | null | undefined): value is string {
  if (value === null || value === undefined || !DATE_PATTERN.test(value)) {
    return false;
  }
  return toIsoDate(parseDate(value)) === value;
}

function parseDate(value: string): Date {
  const [year, month, day] = value.split('-').map(Number);
  return new Date(Date.UTC(year, month - 1, day));
}

function toIsoDate(date: Date): string {
  return date.toISOString().slice(0, 10);
}

export function addDays(date: string, days: number): string {
  return toIsoDate(new Date(parseDate(date).getTime() + days * MS_PER_DAY));
}

/** 0 = Monday … 6 = Sunday. */
export function mondayIndex(date: string): number {
  return (parseDate(date).getUTCDay() + 6) % 7;
}

export function weekStart(date: string): string {
  return addDays(date, -mondayIndex(date));
}

interface Zoned {
  readonly date: string;
  readonly minutes: number;
}

const formatters = new Map<string, Intl.DateTimeFormat>();

function zonedFormatter(timezone: string): Intl.DateTimeFormat {
  let formatter = formatters.get(timezone);
  if (formatter === undefined) {
    formatter = new Intl.DateTimeFormat('en-US', {
      timeZone: timezone,
      hourCycle: 'h23',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
    });
    formatters.set(timezone, formatter);
  }
  return formatter;
}

/** Local date and minutes-since-midnight of an instant in the branch time zone. */
export function zoned(iso: string, timezone: string): Zoned {
  const parts: Record<string, string> = {};
  for (const part of zonedFormatter(timezone).formatToParts(new Date(iso))) {
    parts[part.type] = part.value;
  }
  return {
    date: `${parts['year']}-${parts['month']}-${parts['day']}`,
    minutes: Number(parts['hour']) * 60 + Number(parts['minute']),
  };
}

export function todayInZone(timezone: string, now: Date = new Date()): string {
  return zoned(now.toISOString(), timezone).date;
}

export function nowMinutes(timezone: string, now: Date = new Date()): number {
  return zoned(now.toISOString(), timezone).minutes;
}

export function rangeDays(view: ScheduleView, date: string): readonly string[] {
  if (view === 'day') {
    return [date];
  }
  const start = weekStart(date);
  return Array.from({ length: 7 }, (_, index) => addDays(start, index));
}

export function shiftDate(view: ScheduleView, date: string, direction: -1 | 1): string {
  return addDays(date, direction * (view === 'day' ? 1 : 7));
}

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const WEEKDAYS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

export function weekdayShort(date: string): string {
  return WEEKDAYS[mondayIndex(date)];
}

export function monthDay(date: string): string {
  const parsed = parseDate(date);
  return `${MONTHS[parsed.getUTCMonth()]} ${parsed.getUTCDate()}`;
}

/** BR-20: "MMM d – d, yyyy" (Week) or "EEE, MMM d, yyyy" (Day). */
export function rangeLabel(view: ScheduleView, date: string): string {
  if (view === 'day') {
    return `${weekdayShort(date)}, ${monthDay(date)}, ${parseDate(date).getUTCFullYear()}`;
  }
  const start = parseDate(weekStart(date));
  const end = parseDate(addDays(weekStart(date), 6));
  const year = end.getUTCFullYear();
  const endText =
    start.getUTCMonth() === end.getUTCMonth()
      ? `${end.getUTCDate()}`
      : `${MONTHS[end.getUTCMonth()]} ${end.getUTCDate()}`;
  return `${MONTHS[start.getUTCMonth()]} ${start.getUTCDate()} – ${endText}, ${year}`;
}

/** "HH:mm" or "HH:mm:ss" to "h:mm AM". */
export function formatClock(value: string): string {
  const [hours, minutes] = value.split(':').map(Number);
  return formatMinutes(hours * 60 + minutes);
}

export function formatMinutes(total: number): string {
  const hours = Math.floor(total / 60) % 24;
  const minutes = total % 60;
  const suffix = hours < 12 ? 'AM' : 'PM';
  const display = hours % 12 === 0 ? 12 : hours % 12;
  return `${display}:${String(minutes).padStart(2, '0')} ${suffix}`;
}

export function formatInstant(iso: string, timezone: string): string {
  return formatMinutes(zoned(iso, timezone).minutes);
}

/** "9:30 – 11:30 AM"-style range used on blocks: "h:mm – h:mm". */
export function formatBlockRange(startIso: string, endIso: string, timezone: string): string {
  return `${formatInstant(startIso, timezone)} – ${formatInstant(endIso, timezone)}`;
}

/** BR-04: Saturday/Sunday collapse when no technician has availability and nothing falls on them. */
export function visibleDays(
  days: readonly string[],
  technicians: readonly CalendarTechnician[],
  visits: readonly CalendarVisit[],
  timezone: string,
): readonly string[] {
  if (days.length <= 1) {
    return days;
  }
  return days.filter((day) => {
    if (mondayIndex(day) < 5) {
      return true;
    }
    const hasAvailability = technicians.some((technician) =>
      technician.days.some((entry) => entry.date === day && entry.availability.length > 0),
    );
    const hasAssessment = technicians.some((technician) =>
      technician.assessments.some((range) => zoned(range.start, timezone).date === day),
    );
    const hasVisit = visits.some((visit) => zoned(visit.start, timezone).date === day);
    return hasAvailability || hasAssessment || hasVisit;
  });
}

/** BR-04: whole-hour axis from the earliest availability start to the latest end (Day view). */
export function hourAxis(
  technicians: readonly CalendarTechnician[],
  day: string,
  timezone: string,
  extra: readonly CalendarRange[] = [],
): HourAxis {
  let min = Number.POSITIVE_INFINITY;
  let max = Number.NEGATIVE_INFINITY;
  const consider = (range: CalendarRange): void => {
    min = Math.min(min, zoned(range.start, timezone).minutes);
    max = Math.max(max, zoned(range.end, timezone).minutes);
  };
  for (const technician of technicians) {
    for (const entry of technician.days) {
      if (entry.date === day) {
        entry.availability.forEach(consider);
      }
    }
  }
  extra.forEach(consider);
  let startHour = DEFAULT_AXIS_START_HOUR;
  let endHour = DEFAULT_AXIS_END_HOUR;
  if (Number.isFinite(min) && Number.isFinite(max)) {
    startHour = Math.floor(min / 60);
    endHour = Math.min(24, Math.max(startHour + 1, Math.ceil(max / 60)));
  }
  return {
    startHour,
    endHour,
    hours: Array.from({ length: endHour - startHour + 1 }, (_, index) => startHour + index),
  };
}

/** Inline position of a minutes range on the axis, clamped to it. */
export function blockGeometry(
  startMinutes: number,
  endMinutes: number,
  axis: HourAxis,
): BlockGeometry {
  const axisStart = axis.startHour * 60;
  const span = (axis.endHour - axis.startHour) * 60;
  const from = Math.max(axisStart, startMinutes);
  const to = Math.min(axisStart + span, Math.max(endMinutes, from));
  return {
    left: ((from - axisStart) / span) * 100,
    width: Math.max(((to - from) / span) * 100, 1),
  };
}

export function clockMinutes(value: string): number {
  const [hours, minutes] = value.split(':').map(Number);
  return hours * 60 + minutes;
}

/** BR-05: "<scheduled>h / <available>h", bar tone < 60 % teal, 60–100 % amber, > 100 % red. */
export function loadInfo(load: {
  readonly scheduledMinutes: number;
  readonly availableMinutes: number;
  readonly state: LoadState;
}): LoadInfo {
  if (load.state === 'no_availability') {
    return { text: 'No availability', tone: 'red', percent: 100 };
  }
  const hours = (minutes: number): string => `${Math.round((minutes / 60) * 10) / 10}h`;
  const text = `${hours(load.scheduledMinutes)} / ${hours(load.availableMinutes)}`;
  if (load.state === 'none' || load.availableMinutes <= 0) {
    return { text, tone: 'none', percent: 0 };
  }
  const percent = (load.scheduledMinutes / load.availableMinutes) * 100;
  const tone: LoadTone = percent < 60 ? 'teal' : percent <= 100 ? 'amber' : 'red';
  return { text, tone, percent: Math.min(100, Math.round(percent)) };
}

/** Proposed block as minutes on its local day, when the three inputs are present and ordered. */
export function proposedMinutes(slot: {
  readonly date: string | null;
  readonly start: string | null;
  readonly end: string | null;
}): { readonly date: string; readonly startMinutes: number; readonly endMinutes: number } | null {
  if (!isValidDate(slot.date) || !slot.start || !slot.end) {
    return null;
  }
  const startMinutes = clockMinutes(slot.start);
  const endMinutes = clockMinutes(slot.end);
  return endMinutes > startMinutes ? { date: slot.date, startMinutes, endMinutes } : null;
}

/** "#<WO-n>" whether or not the API already carries the hash. */
export function numberText(displayNumber: string): string {
  return displayNumber.startsWith('#') ? displayNumber : `#${displayNumber}`;
}

/** BR-07: "EEE, MMM d, h:mm a – h:mm a" in the branch time zone, or "No preferred date". */
export function formatPreferred(
  startIso: string | null,
  endIso: string | null,
  timezone: string,
): string {
  if (startIso === null || endIso === null) {
    return 'No preferred date';
  }
  const date = zoned(startIso, timezone).date;
  return `${weekdayShort(date)}, ${monthDay(date)}, ${formatBlockRange(startIso, endIso, timezone)}`;
}
