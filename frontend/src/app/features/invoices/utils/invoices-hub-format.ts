import {
  DisplayStatus,
  InvoiceRow,
  LastActivity,
  NOTE_MAX_LENGTH,
  PaymentMethod,
  REFERENCE_MAX_LENGTH,
  REFERENCE_REQUIRED_METHODS,
} from '../models/invoices-hub.model';
import { formatCalendarDate, money } from '../../billing-review/utils/billing-review-format';

import { localized, t } from '../../../core/i18n/translate';
export {
  addDays,
  formatCalendarDate,
  formatInstantDate,
  money,
  todayIn,
} from '../../billing-review/utils/billing-review-format';
import { activeIntlLocale } from '../../../core/i18n/active-locale';

/** BR-03 chip: text and icon so the status never relies on colour alone (BR-30). */
export const STATUS_CHIPS: Readonly<
  Record<DisplayStatus, { readonly label: string; readonly icon: string; readonly tone: string }>
> = localized(() => ({
  draft: { label: t('invoices.invoicesHubFormat.draft'), icon: 'pi-file-edit', tone: 'neutral' },
  sent: { label: t('invoices.invoicesHubFormat.sent'), icon: 'pi-send', tone: 'info' },
  partially_paid: {
    label: t('invoices.invoicesHubFormat.partiallyPaid'),
    icon: 'pi-clock',
    tone: 'warning',
  },
  paid: { label: t('invoices.invoicesHubFormat.paid'), icon: 'pi-check-circle', tone: 'success' },
  overdue: {
    label: t('invoices.invoicesHubFormat.overdue'),
    icon: 'pi-exclamation-circle',
    tone: 'danger',
  },
}));

export function statusText(row: Pick<InvoiceRow, 'status' | 'daysOverdue'>): string {
  if (row.status !== 'overdue') {
    return STATUS_CHIPS[row.status].label;
  }
  const days = row.daysOverdue ?? 0;
  return t('invoices.invoicesHubFormat.overdue2', {
    days: days,
    value: days === 1 ? t('invoices.invoicesHubFormat.day') : t('invoices.invoicesHubFormat.days'),
  });
}

/** "MMM d" of a calendar date (`yyyy-mm-dd`), never shifted by a time zone. */
function shortDate(value: string): string {
  return new Intl.DateTimeFormat(activeIntlLocale(), {
    month: 'short',
    day: 'numeric',
    timeZone: 'UTC',
  }).format(new Date(`${value}T12:00:00Z`));
}

/** BR-08 labels: "Paid MMM d, yyyy", "Payment MMM d", "Sent MMM d, yyyy", "—". */
export function lastActivityText(activity: LastActivity | null): string {
  switch (activity?.kind) {
    case 'paid':
      return t('invoices.invoicesHubFormat.paid2', { date: formatCalendarDate(activity.date) });
    case 'payment':
      return t('invoices.invoicesHubFormat.payment', { date: shortDate(activity.date) });
    case 'sent':
      return t('invoices.invoicesHubFormat.sent2', { date: formatCalendarDate(activity.date) });
    default:
      return '—';
  }
}

/** "<n> days" for the average time to pay, "—" when there is none (BR-21). */
export function averageDaysText(days: number | null): string {
  return days === null
    ? '—'
    : `${days} ${days === 1 ? t('invoices.invoicesHubFormat.day') : t('invoices.invoicesHubFormat.days')}`;
}

/** "Showing <a> – <b> of <n> <noun>" (BR-22). */
export function showingText(page: number, pageSize: number, total: number, noun: string): string {
  const first = (page - 1) * pageSize + 1;
  const last = Math.min(page * pageSize, total);
  return t('invoices.invoicesHubFormat.showingOf', {
    first: first,
    last: last,
    total: total,
    noun: noun,
  });
}

export type PaymentField =
  'amount' | 'paidDate' | 'method' | 'reference' | 'receivedByUserId' | 'note';
export type PaymentErrors = Partial<Record<PaymentField, string>>;

/** Drawer fields in DOM order: the first invalid one receives focus (BR-26). */
export const PAYMENT_FIELD_ORDER: readonly PaymentField[] = [
  'amount',
  'paidDate',
  'method',
  'reference',
  'receivedByUserId',
  'note',
];

export interface PaymentValues {
  readonly amount: string;
  readonly paidDate: string;
  readonly method: PaymentMethod | null;
  readonly reference: string;
  readonly receivedByUserId: string;
  readonly note: string;
}

export interface PaymentContext {
  readonly balance: number;
  readonly currency: string;
  readonly issueDate: string;
  readonly today: string;
}

/** Parses an amount with at most two decimals; `null` when it is not one. */
export function parseAmount(text: string): number | null {
  const value = text.trim();
  return /^\d+(\.\d{1,2})?$/.test(value) ? Number(value) : null;
}

/** Payment field rules, client side (the backend decides). */
export function validatePayment(values: PaymentValues, context: PaymentContext): PaymentErrors {
  const errors: PaymentErrors = {};
  const amount = parseAmount(values.amount);
  if (amount === null || amount < 0.01) {
    errors.amount = t('invoices.invoicesHubFormat.enterAnAmountGreater');
  } else if (amount > context.balance) {
    errors.amount = t('invoices.invoicesHubFormat.amountCantExceedThe', {
      value: money(context.balance, context.currency),
    });
  }
  if (values.paidDate === '') {
    errors.paidDate = t('invoices.invoicesHubFormat.enterThePaymentDate');
  } else if (values.paidDate > context.today) {
    errors.paidDate = t('invoices.invoicesHubFormat.paymentDateCantBe');
  } else if (values.paidDate < context.issueDate) {
    errors.paidDate = t('invoices.invoicesHubFormat.paymentDateCantBe2');
  }
  if (values.method === null) {
    errors.method = t('invoices.invoicesHubFormat.selectAPaymentMethod');
  }
  const reference = values.reference.trim();
  if (reference.length > REFERENCE_MAX_LENGTH) {
    errors.reference = t('invoices.invoicesHubFormat.useCharactersOrFewer', {
      REFERENCE_MAX_LENGTH: REFERENCE_MAX_LENGTH,
    });
  } else if (
    reference === '' &&
    values.method !== null &&
    REFERENCE_REQUIRED_METHODS.includes(values.method)
  ) {
    errors.reference = t('invoices.invoicesHubFormat.enterTheReferenceNumber');
  }
  if (values.receivedByUserId === '') {
    errors.receivedByUserId = t('invoices.invoicesHubFormat.selectWhoReceivedThe');
  }
  if (values.note.trim().length > NOTE_MAX_LENGTH) {
    errors.note = t('invoices.invoicesHubFormat.useCharactersOrFewer2', {
      NOTE_MAX_LENGTH: NOTE_MAX_LENGTH,
    });
  }
  return errors;
}

export function firstInvalidField(errors: PaymentErrors): PaymentField | null {
  return PAYMENT_FIELD_ORDER.find((field) => errors[field] !== undefined) ?? null;
}
