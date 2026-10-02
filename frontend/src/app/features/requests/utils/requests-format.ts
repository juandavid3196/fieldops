import { formatDate, formatTime } from '../../customers/utils/customer-format';
import { todayInTimeZone } from '../../service-request/service-request.validators';
import {
  RequestAvatar,
  RequestCard,
  RequestDetail,
  RequestStatus,
  STATUS_LABELS,
  TIME_WINDOW_LABELS,
} from '../models/requests.model';

const DATE_ONLY = /^\d{4}-\d{2}-\d{2}$/;

/** A bare `YYYY-MM-DD` is a calendar date, never shifted by a time zone. */
function dateOnly(value: string): string {
  return formatDate(`${value}T12:00:00Z`, 'UTC');
}

function day(value: string, timeZone: string): string {
  return DATE_ONLY.test(value) ? dateOnly(value) : formatDate(value, timeZone);
}

export function dateTime(iso: string, timeZone: string): string {
  return `${formatDate(iso, timeZone)} ${formatTime(iso, timeZone)}`;
}

/** BR-04 card date line. */
export function cardDate(card: RequestCard, timeZone: string): string {
  switch (card.dateKind) {
    case 'assessment':
      return card.date === null ? '' : dateTime(card.date, timeZone);
    case 'asap':
      return 'ASAP';
    case 'flexible':
      return 'Flexible';
    default:
      return card.date === null ? '' : day(card.date, timeZone);
  }
}

/** BR-04 age: calendar days since creation in the organization timezone. */
export function ageLabel(createdAt: string, timeZone: string, now: Date = new Date()): string {
  const created = new Date(createdAt);
  if (Number.isNaN(created.getTime())) {
    return '';
  }
  const toUtc = (key: string): number => {
    const [year, month, date] = key.split('-').map(Number);
    return Date.UTC(year, month - 1, date);
  };
  const days = Math.max(
    0,
    Math.round(
      (toUtc(todayInTimeZone(timeZone, now)) - toUtc(todayInTimeZone(timeZone, created))) /
        86_400_000,
    ),
  );
  return days === 0 ? 'Today' : `${days}d`;
}

/** BR-19 time: "Today at h:mm a", otherwise "MMM d, yyyy h:mm a". */
export function activityTime(iso: string, timeZone: string, now: Date = new Date()): string {
  const sameDay = todayInTimeZone(timeZone, new Date(iso)) === todayInTimeZone(timeZone, now);
  return sameDay ? `Today at ${formatTime(iso, timeZone)}` : dateTime(iso, timeZone);
}

export function avatarLabel(avatar: RequestAvatar): string {
  return `${avatar.role === 'technician' ? 'Assessment technician' : 'Assignee'}: ${avatar.name}`;
}

/** Accessible name of a card: "<title>, <customer>, <status>". */
export function cardName(card: RequestCard, status: RequestStatus): string {
  return `${card.title}, ${card.customerName ?? 'Unknown customer'}, ${STATUS_LABELS[status]}`;
}

/** BR-08 service address line from the structured or plain address. */
export function addressLine(address: RequestDetail['serviceAddress']): string | null {
  if (address === null || typeof address === 'string') {
    return address;
  }
  const region = [address.state, address.postalCode].filter((part) => !!part).join(' ');
  const line = [address.line1, address.line2, address.city, region]
    .filter((part) => !!part)
    .join(', ');
  return line === '' ? null : line;
}

/** BR-08 preferred visit line. */
export function preferredVisit(availability: RequestDetail['availability']): string | null {
  if (availability === null) {
    return null;
  }
  if (availability.dateMode === 'asap') {
    return 'ASAP';
  }
  if (availability.dateMode === 'flexible') {
    return 'Flexible';
  }
  const date = availability.preferredDate === null ? '' : dateOnly(availability.preferredDate);
  const window =
    availability.timeWindow === null ? '' : TIME_WINDOW_LABELS[availability.timeWindow];
  return [date, window].filter((part) => part !== '').join(', ') || null;
}

export function fileSize(bytes: number): string {
  if (bytes >= 1024 * 1024) {
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }
  return `${Math.max(1, Math.round(bytes / 1024))} KB`;
}

export function initialsOf(name: string): string {
  return name
    .trim()
    .split(/\s+/)
    .slice(0, 2)
    .map((word) => word.charAt(0))
    .join('')
    .toUpperCase();
}

/** `YYYY-MM-DDTHH:mm` of an instant in the organization timezone (assessment form values). */
export function toLocalInput(iso: string, timeZone: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return '';
  }
  const options: Intl.DateTimeFormatOptions = {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
  };
  let formatter: Intl.DateTimeFormat;
  try {
    formatter = new Intl.DateTimeFormat('en-US', { ...options, timeZone });
  } catch {
    formatter = new Intl.DateTimeFormat('en-US', { ...options, timeZone: 'UTC' });
  }
  const parts = Object.fromEntries(formatter.formatToParts(date).map((p) => [p.type, p.value]));
  return `${parts['year']}-${parts['month']}-${parts['day']}T${parts['hour']}:${parts['minute']}`;
}
