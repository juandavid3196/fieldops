import {
  CustomerContact,
  CustomerType,
  InvoiceStatus,
  PropertyItem,
} from '../models/customer.model';
import { formatDate, formatTime } from './customer-format';

/** BR-03 avatar initials. */
export function initials(
  type: CustomerType,
  displayName: string,
  contact: Pick<CustomerContact, 'firstName' | 'lastName'>,
): string {
  const first = (word: string | undefined): string => (word ?? '').charAt(0);
  const letters =
    type === 'residential'
      ? `${first(contact.firstName.trim())}${first(contact.lastName.trim())}`
      : displayName
          .trim()
          .split(/\s+/)
          .slice(0, 2)
          .map((word) => first(word))
          .join('');
  return letters.toUpperCase();
}

/** BR-03 preferred contact label. */
export function preferredContact(contact: Pick<CustomerContact, 'prefersEmail' | 'prefersSms'>) {
  return contact.prefersEmail && contact.prefersSms
    ? 'Email & SMS'
    : contact.prefersSms
      ? 'SMS'
      : 'Email';
}

/** BR-04 one-line address: "<line1>[, <line2>], <city>[, <state>] [<zip>]". */
export function addressLine(property: PropertyItem): string {
  const line2 = property.addressLine2?.trim();
  const state = property.stateRegion?.trim();
  const zip = property.postalCode?.trim();
  const region = [state, zip].filter((part) => !!part).join(' ');
  return [property.addressLine1, line2, property.city, region].filter((part) => !!part).join(', ');
}

/** "approved_for_billing" -> "Approved for billing". */
export function sentenceCase(value: string): string {
  const text = value.replace(/_/g, ' ').trim().toLowerCase();
  return text.charAt(0).toUpperCase() + text.slice(1);
}

/** "MMM d, yyyy · h:mm a". */
export function formatDateTime(iso: string, timeZone: string): string {
  return `${formatDate(iso, timeZone)} · ${formatTime(iso, timeZone)}`;
}

/** "MMM d, yyyy · h:mm a – h:mm a" (end omitted when null). */
export function formatRange(startsAt: string, endsAt: string | null | undefined, zone: string) {
  const start = formatDateTime(startsAt, zone);
  return endsAt ? `${start} – ${formatTime(endsAt, zone)}` : start;
}

/** "Since MMM yyyy" caption in the organization timezone. */
export function sinceLabel(iso: string, timeZone: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return '';
  }
  const format = (zone: string): string =>
    new Intl.DateTimeFormat('en-US', { month: 'short', year: 'numeric', timeZone: zone }).format(
      date,
    );
  try {
    return `Since ${format(timeZone)}`;
  } catch {
    return `Since ${format('UTC')}`;
  }
}

export const INVOICE_STATUS_LABELS: Readonly<Record<InvoiceStatus, string>> = {
  sent: 'Sent',
  partially_paid: 'Partially paid',
  paid: 'Paid',
  overdue: 'Overdue',
};

export function invoiceSeverity(status: InvoiceStatus): 'success' | 'danger' | 'info' | 'warn' {
  return status === 'paid'
    ? 'success'
    : status === 'overdue'
      ? 'danger'
      : status === 'partially_paid'
        ? 'warn'
        : 'info';
}

/** First non-empty line of a text. */
export function firstLine(value: string | null | undefined): string {
  return (value ?? '').split(/\r?\n/)[0].trim();
}

/** BR-16 / BR-05 trimmed max-length check for the note input. */
export function validateNote(value: string): string | null {
  const text = value.trim();
  if (text.length === 0) {
    return 'Enter a note.';
  }
  return text.length > 2000 ? 'Use 2,000 characters or fewer.' : null;
}

/** BR-17 activity label from the action and the subject (current property name). */
export function activityLabel(action: string, subject: string | null | undefined): string {
  const name = subject?.trim() ?? '';
  switch (action) {
    case 'customer.created':
      return 'Customer created';
    case 'customer.updated':
      return 'Customer details updated';
    case 'customer.archived':
      return 'Customer archived';
    case 'customer.reactivated':
      return 'Customer reactivated';
    case 'property.created':
      return `Property added: ${name}`;
    case 'property.updated':
      return `Property updated: ${name}`;
    case 'property.primary_changed':
      return `Primary property changed to ${name}`;
    case 'property.archived':
      return `Property archived: ${name}`;
    case 'property.reactivated':
      return `Property reactivated: ${name}`;
    case 'customer_note.created':
      return 'Internal note added';
    default:
      return /^[a-z_]+\.[a-z_]+$/.test(action) ? sentenceCase(action.replace('.', ' ')) : action;
  }
}

/** BR-03: organization currency with two decimals, always. */
export function formatMoney(value: number, currency: string | null): string {
  const options = { minimumFractionDigits: 2, maximumFractionDigits: 2 };
  try {
    return new Intl.NumberFormat(
      'en-US',
      currency === null ? options : { style: 'currency', currency, ...options },
    ).format(value);
  } catch {
    return new Intl.NumberFormat('en-US', options).format(value);
  }
}
