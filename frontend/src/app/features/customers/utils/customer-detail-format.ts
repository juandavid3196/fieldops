import {
  CustomerContact,
  CustomerType,
  InvoiceStatus,
  PropertyItem,
} from '../models/customer.model';
import { formatDate, formatTime } from './customer-format';

import { localized, t } from '../../../core/i18n/translate';
import { activeIntlLocale } from '../../../core/i18n/active-locale';
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
    ? t('customers.customerDetailFormat.emailSms')
    : contact.prefersSms
      ? 'SMS'
      : t('common.email');
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
    new Intl.DateTimeFormat(activeIntlLocale(), {
      month: 'short',
      year: 'numeric',
      timeZone: zone,
    }).format(date);
  try {
    return t('customers.customerDetailFormat.since', { timeZone: format(timeZone) });
  } catch {
    return t('customers.customerDetailFormat.since2', { value: format('UTC') });
  }
}

export const INVOICE_STATUS_LABELS: Readonly<Record<InvoiceStatus, string>> = localized(() => ({
  sent: t('customers.customerDetailFormat.sent'),
  partially_paid: t('customers.customerDetailFormat.partiallyPaid'),
  paid: t('customers.customerDetailFormat.paid'),
  overdue: t('customers.customerDetailFormat.overdue'),
}));

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
    return t('customers.customerDetailFormat.enterANote');
  }
  return text.length > 2000 ? t('customers.customerDetailFormat.use2000Characters') : null;
}

/** BR-03: organization currency with two decimals, always. */
export function formatMoney(value: number, currency: string | null): string {
  const options = { minimumFractionDigits: 2, maximumFractionDigits: 2 };
  try {
    return new Intl.NumberFormat(
      activeIntlLocale(),
      currency === null ? options : { style: 'currency', currency, ...options },
    ).format(value);
  } catch {
    return new Intl.NumberFormat(activeIntlLocale(), options).format(value);
  }
}
