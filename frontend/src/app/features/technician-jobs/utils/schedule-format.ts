import { ScheduleDay, ScheduleVisit } from '../models/technician-visits.model';
import { formatScheduledHours, pluralLabel } from './technician-format';

const DAY_MS = 86_400_000;

function utcNoon(date: string): Date {
  return new Date(`${date}T12:00:00Z`);
}

/** `date` (`YYYY-MM-DD`) shifted by whole days; calendar arithmetic, independent of any zone. */
export function addDays(date: string, days: number): string {
  return new Date(utcNoon(date).getTime() + days * DAY_MS).toISOString().slice(0, 10);
}

/** 0 for Monday … 6 for Sunday. */
export function weekdayIndex(date: string): number {
  return (utcNoon(date).getUTCDay() + 6) % 7;
}

function calendarText(date: string, options: Intl.DateTimeFormatOptions): string {
  return new Intl.DateTimeFormat('en-US', { ...options, timeZone: 'UTC' }).format(utcNoon(date));
}

/** "EEE" */
export function dayName(date: string): string {
  return calendarText(date, { weekday: 'short' });
}

export function dayNumber(date: string): string {
  return calendarText(date, { day: 'numeric' });
}

/** "EEEE, MMM d" (BR-16). */
export function dayHeading(date: string): string {
  return calendarText(date, { weekday: 'long', month: 'short', day: 'numeric' });
}

/** Local calendar day (`YYYY-MM-DD`) of an instant in `timeZone`. */
export function localDateOf(iso: string, timeZone: string): string {
  return new Intl.DateTimeFormat('en-CA', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    timeZone,
  }).format(new Date(iso));
}

/** BR-04: "<n> job(s) · <h>h"; "0 jobs" when the day is empty. */
export function summaryText(day: ScheduleDay | undefined): string {
  const count = day?.visitCount ?? 0;
  const label = `${count} ${pluralLabel(count, 'job', 'jobs')}`;
  return count === 0 ? label : `${label} · ${formatScheduledHours(day?.scheduledMinutes ?? 0)}h`;
}

/** BR-16: "1h 30m", "2h", "45m". */
export function durationText(minutes: number): string {
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  return [hours > 0 ? `${hours}h` : '', rest > 0 || hours === 0 ? `${rest}m` : '']
    .filter((part) => part !== '')
    .join(' ');
}

/** "city, state postal" (BR-16). */
export function cityLine(address: ScheduleVisit['address']): string {
  const regionPostal = [address.stateRegion, address.postalCode]
    .map((part) => part?.trim() ?? '')
    .filter((part) => part !== '')
    .join(' ');
  return [address.city, regionPostal]
    .map((part) => part.trim())
    .filter((part) => part !== '')
    .join(', ');
}

/** BR-17: "just now", "<n> min ago", "<n> hours ago", "yesterday", "<n> days ago". */
export function relativeTimeText(iso: string | null | undefined, now: Date): string | null {
  if (!iso) {
    return null;
  }
  const minutes = Math.floor((now.getTime() - new Date(iso).getTime()) / 60_000);
  if (Number.isNaN(minutes)) {
    return null;
  }
  if (minutes < 1) {
    return 'just now';
  }
  if (minutes < 60) {
    return `${minutes} min ago`;
  }
  const hours = Math.floor(minutes / 60);
  if (hours < 24) {
    return `${hours} ${pluralLabel(hours, 'hour', 'hours')} ago`;
  }
  const days = Math.floor(hours / 24);
  return days === 1 ? 'yesterday' : `${days} days ago`;
}

/** BR-17 status line: "Started …", "On the way since …", "Paused …"; `null` when unknown. */
export function statusSinceText(
  status: string,
  since: string | null | undefined,
  now: Date,
): string | null {
  const relative = relativeTimeText(since, now);
  if (relative === null) {
    return null;
  }
  switch (status) {
    case 'in_progress':
      return `Started ${relative}`;
    case 'on_the_way':
      return `On the way since ${relative}`;
    case 'paused':
      return `Paused ${relative}`;
    default:
      return null;
  }
}
