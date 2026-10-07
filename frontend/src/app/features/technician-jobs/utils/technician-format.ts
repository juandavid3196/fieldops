import { TodayVisit, TodayVisitStatus, VisitAddress } from '../models/technician-visits.model';

/** Every date and time is formatted in the response time zone, never the device zone. */

function normalizeSpaces(value: string): string {
  return value.replace(/\p{Zs}/gu, ' ');
}

/** "h:mm a" */
export function formatTime(iso: string, timeZone: string): string {
  return normalizeSpaces(
    new Intl.DateTimeFormat('en-US', { hour: 'numeric', minute: '2-digit', timeZone }).format(
      new Date(iso),
    ),
  );
}

export function formatTimeRange(start: string, end: string, timeZone: string): string {
  return `${formatTime(start, timeZone)} – ${formatTime(end, timeZone)}`;
}

/** Arrival line text, or `null` when there is no window (BR-10). */
export function arrivalWindowText(visit: TodayVisit, timeZone: string): string | null {
  if (visit.arrivalWindowStart === null || visit.arrivalWindowEnd === null) {
    return null;
  }
  const start = formatTime(visit.arrivalWindowStart, timeZone);
  const end = formatTime(visit.arrivalWindowEnd, timeZone);
  return start === end ? `Arrival at ${start}` : `Arrival window: ${start} – ${end}`;
}

function formatCalendarDate(date: string, options: Intl.DateTimeFormatOptions): string {
  // `date` is already the local calendar day; noon UTC keeps it stable in any zone.
  return new Intl.DateTimeFormat('en-US', { ...options, timeZone: 'UTC' }).format(
    new Date(`${date}T12:00:00Z`),
  );
}

/** "EEEE, MMM d, yyyy" */
export function formatLongDate(date: string): string {
  return formatCalendarDate(date, {
    weekday: 'long',
    month: 'short',
    day: 'numeric',
    year: 'numeric',
  });
}

/** "EEE, MMM d" */
export function formatShortDate(date: string): string {
  return formatCalendarDate(date, { weekday: 'short', month: 'short', day: 'numeric' });
}

/** "EEE, MMM d" of an instant in the response time zone. */
export function formatInstantShortDate(iso: string, timeZone: string): string {
  return new Intl.DateTimeFormat('en-US', {
    weekday: 'short',
    month: 'short',
    day: 'numeric',
    timeZone,
  }).format(new Date(iso));
}

/** BR-06: 05:00-11:59 morning, 12:00-17:59 afternoon, otherwise evening, by local hour. */
export function greetingFor(now: Date, timeZone: string): string {
  const hour = Number(
    new Intl.DateTimeFormat('en-US', { hour: 'numeric', hourCycle: 'h23', timeZone }).format(now),
  );
  if (hour >= 5 && hour < 12) {
    return 'Good morning';
  }
  return hour >= 12 && hour < 18 ? 'Good afternoon' : 'Good evening';
}

/** BR-07: one decimal, trailing ".0" dropped. */
export function formatScheduledHours(minutes: number): string {
  return (Math.round(minutes / 6) / 10).toFixed(1).replace(/\.0$/, '');
}

export function pluralLabel(count: number | string, singular: string, plural: string): string {
  return Number(count) === 1 ? singular : plural;
}

/** BR-08 */
export function progressPercent(completed: number, jobs: number): number {
  return jobs === 0 ? 0 : Math.round((completed / jobs) * 100);
}

/** BR-14: "just now" under one minute, then "<n> min ago". */
export function updatedText(updatedAt: number, now: number): string {
  const minutes = Math.max(0, Math.floor((now - updatedAt) / 60000));
  return minutes < 1 ? 'Updated just now' : `Updated ${minutes} min ago`;
}

/** BR-10: only Urgent and High show a chip. */
export function priorityLabel(priority: number | string): string | null {
  if (priority === 1 || priority === 'urgent') {
    return 'Urgent';
  }
  return priority === 2 || priority === 'high' ? 'High priority' : null;
}

export function addressLine(address: VisitAddress): string {
  const regionPostal = [address.stateRegion, address.postalCode]
    .map((part) => part?.trim() ?? '')
    .filter((part) => part !== '')
    .join(' ');
  return [address.line1, address.city, regionPostal]
    .map((part) => part.trim())
    .filter((part) => part !== '')
    .join(', ');
}

/** BR-12 */
export function directionsUrl(visit: TodayVisit): string {
  const destination =
    visit.latitude !== null && visit.longitude !== null
      ? `${visit.latitude},${visit.longitude}`
      : encodeURIComponent(addressLine(visit.address));
  return `https://www.google.com/maps/dir/?api=1&destination=${destination}`;
}

export function telUrl(phone: string): string {
  return `tel:${phone.replace(/[^\d+]/g, '')}`;
}

export const STATUS_LABELS: Readonly<Record<TodayVisitStatus, string>> = {
  completed: 'Completed',
  approved: 'Completed',
  scheduled: 'Scheduled',
  assigned: 'Scheduled',
  on_the_way: 'On the way',
  in_progress: 'In progress',
  paused: 'Paused',
  needs_correction: 'Needs correction',
};

export type StatusSeverity = 'success' | 'info' | 'warn' | 'secondary' | 'danger';

export const STATUS_SEVERITIES: Readonly<Record<TodayVisitStatus, StatusSeverity>> = {
  completed: 'success',
  approved: 'success',
  scheduled: 'secondary',
  assigned: 'secondary',
  on_the_way: 'info',
  in_progress: 'info',
  paused: 'warn',
  needs_correction: 'danger',
};

export function isCompleted(status: TodayVisitStatus): boolean {
  return status === 'completed' || status === 'approved';
}

/** Readable text color over an arbitrary avatar background. */
export function avatarTextColor(hex: string): string {
  const match = /^#?([0-9a-f]{6})$/i.exec(hex);
  if (match === null) {
    return '#fff';
  }
  const value = parseInt(match[1], 16);
  const luminance =
    0.299 * ((value >> 16) & 255) + 0.587 * ((value >> 8) & 255) + 0.114 * (value & 255);
  return luminance > 160 ? '#102a43' : '#fff';
}
