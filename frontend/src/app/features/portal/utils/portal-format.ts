import { formatMoney } from '../../customers/utils/customer-detail-format';
import { dateOnlyLabel } from '../../quotes/utils/quote-format';

export { dateOnlyLabel as dateLabel, formatMoney as money };

const MINUTE_MS = 60_000;
const HOUR_MS = 3_600_000;
const DAY_MS = 86_400_000;

/** BR-17: greeting by the browser local time. */
export function greeting(now: Date, firstName: string): string {
  const hour = now.getHours();
  const part = hour < 12 ? 'morning' : hour < 18 ? 'afternoon' : 'evening';
  return `Good ${part}, ${firstName}`;
}

/** "EEEE, MMM d, yyyy" of a calendar date (`YYYY-MM-DD`), never shifted by a time zone. */
export function longDateLabel(value: string): string {
  const date = new Date(`${value}T12:00:00Z`);
  if (Number.isNaN(date.getTime())) {
    return value;
  }
  return new Intl.DateTimeFormat('en-US', {
    weekday: 'long',
    month: 'short',
    day: 'numeric',
    year: 'numeric',
    timeZone: 'UTC',
  }).format(date);
}

/** "h:mm AM" of an `HH:mm` time. */
export function timeLabel(value: string): string {
  const [hours, minutes] = value.split(':').map(Number);
  if (hours === undefined || minutes === undefined || Number.isNaN(hours + minutes)) {
    return value;
  }
  const suffix = hours >= 12 ? 'PM' : 'AM';
  const hour = hours % 12 === 0 ? 12 : hours % 12;
  return `${hour}:${String(minutes).padStart(2, '0')} ${suffix}`;
}

export function timeRange(start: string, end: string): string {
  return `${timeLabel(start)} – ${timeLabel(end)}`;
}

/** BR-24 time label: relative under 24 h, "Yesterday" under 48 h, else "MMM d, yyyy". */
export function updateTimeLabel(occurredAt: string, now: Date): string {
  const at = new Date(occurredAt);
  const elapsed = now.getTime() - at.getTime();
  if (Number.isNaN(elapsed)) {
    return '';
  }
  if (elapsed < MINUTE_MS) {
    return 'Just now';
  }
  if (elapsed < HOUR_MS) {
    return `${Math.floor(elapsed / MINUTE_MS)}m ago`;
  }
  if (elapsed < DAY_MS) {
    return `${Math.floor(elapsed / HOUR_MS)}h ago`;
  }
  if (elapsed < 2 * DAY_MS) {
    return 'Yesterday';
  }
  return new Intl.DateTimeFormat('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
  }).format(at);
}

export function initialsOf(...parts: readonly string[]): string {
  const letters = parts
    .flatMap((part) => part.trim().split(/\s+/))
    .filter((word) => word !== '')
    .map((word) => word.charAt(0).toUpperCase());
  return (letters.length > 2 ? [letters[0], letters[letters.length - 1]] : letters).join('') || '?';
}

/** `tel:` link of a phone number. */
export function telHref(phone: string): string {
  return `tel:${phone.replace(/[^\d+]/g, '')}`;
}

/** "<name> — <address line 1>" option label of the property selector (BR-18). */
export function propertyLabel(property: { name: string; addressLine1: string }): string {
  return `${property.name} — ${property.addressLine1}`;
}

/** Tomorrow … today + 90 days (BR-32), `YYYY-MM-DD` in the browser calendar. */
export function rescheduleRange(now: Date = new Date()): { min: Date; max: Date } {
  const start = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  return {
    min: new Date(start.getFullYear(), start.getMonth(), start.getDate() + 1),
    max: new Date(start.getFullYear(), start.getMonth(), start.getDate() + 90),
  };
}

export function toDateOnly(date: Date): string {
  return [
    date.getFullYear(),
    String(date.getMonth() + 1).padStart(2, '0'),
    String(date.getDate()).padStart(2, '0'),
  ].join('-');
}
