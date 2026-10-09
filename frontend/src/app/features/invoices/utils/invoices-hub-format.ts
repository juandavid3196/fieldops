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

export {
  addDays,
  formatCalendarDate,
  formatInstantDate,
  money,
  todayIn,
} from '../../billing-review/utils/billing-review-format';

/** BR-03 chip: text and icon so the status never relies on colour alone (BR-30). */
export const STATUS_CHIPS: Readonly<
  Record<DisplayStatus, { readonly label: string; readonly icon: string; readonly tone: string }>
> = {
  draft: { label: 'Draft', icon: 'pi-file-edit', tone: 'neutral' },
  sent: { label: 'Sent', icon: 'pi-send', tone: 'info' },
  partially_paid: { label: 'Partially paid', icon: 'pi-clock', tone: 'warning' },
  paid: { label: 'Paid', icon: 'pi-check-circle', tone: 'success' },
  overdue: { label: 'Overdue', icon: 'pi-exclamation-circle', tone: 'danger' },
};

export function statusText(row: Pick<InvoiceRow, 'status' | 'daysOverdue'>): string {
  if (row.status !== 'overdue') {
    return STATUS_CHIPS[row.status].label;
  }
  const days = row.daysOverdue ?? 0;
  return `Overdue ${days} ${days === 1 ? 'day' : 'days'}`;
}

/** "MMM d" of a calendar date (`yyyy-mm-dd`), never shifted by a time zone. */
function shortDate(value: string): string {
  return new Intl.DateTimeFormat('en-US', {
    month: 'short',
    day: 'numeric',
    timeZone: 'UTC',
  }).format(new Date(`${value}T12:00:00Z`));
}

/** BR-08 labels: "Paid MMM d, yyyy", "Payment MMM d", "Sent MMM d, yyyy", "—". */
export function lastActivityText(activity: LastActivity | null): string {
  switch (activity?.kind) {
    case 'paid':
      return `Paid ${formatCalendarDate(activity.date)}`;
    case 'payment':
      return `Payment ${shortDate(activity.date)}`;
    case 'sent':
      return `Sent ${formatCalendarDate(activity.date)}`;
    default:
      return '—';
  }
}

/** "<n> days" for the average time to pay, "—" when there is none (BR-21). */
export function averageDaysText(days: number | null): string {
  return days === null ? '—' : `${days} ${days === 1 ? 'day' : 'days'}`;
}

/** "Showing <a> – <b> of <n> <noun>" (BR-22). */
export function showingText(page: number, pageSize: number, total: number, noun: string): string {
  const first = (page - 1) * pageSize + 1;
  const last = Math.min(page * pageSize, total);
  return `Showing ${first} – ${last} of ${total} ${noun}`;
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
    errors.amount = 'Enter an amount greater than 0.';
  } else if (amount > context.balance) {
    errors.amount = `Amount can't exceed the outstanding balance of ${money(context.balance, context.currency)}.`;
  }
  if (values.paidDate === '') {
    errors.paidDate = 'Enter the payment date.';
  } else if (values.paidDate > context.today) {
    errors.paidDate = "Payment date can't be in the future.";
  } else if (values.paidDate < context.issueDate) {
    errors.paidDate = "Payment date can't be before the invoice date.";
  }
  if (values.method === null) {
    errors.method = 'Select a payment method.';
  }
  const reference = values.reference.trim();
  if (reference.length > REFERENCE_MAX_LENGTH) {
    errors.reference = `Use ${REFERENCE_MAX_LENGTH} characters or fewer.`;
  } else if (
    reference === '' &&
    values.method !== null &&
    REFERENCE_REQUIRED_METHODS.includes(values.method)
  ) {
    errors.reference = 'Enter the reference number.';
  }
  if (values.receivedByUserId === '') {
    errors.receivedByUserId = 'Select who received the payment.';
  }
  if (values.note.trim().length > NOTE_MAX_LENGTH) {
    errors.note = `Use ${NOTE_MAX_LENGTH} characters or fewer.`;
  }
  return errors;
}

export function firstInvalidField(errors: PaymentErrors): PaymentField | null {
  return PAYMENT_FIELD_ORDER.find((field) => errors[field] !== undefined) ?? null;
}
