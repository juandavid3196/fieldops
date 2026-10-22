import { PublicInvoice, PublicPayment } from '../models/invoice.model';
import { STATUS_CHIPS, formatCalendarDate, money, todayIn } from './invoices-hub-format';

import { localized, t } from '../../../core/i18n/translate';
export type StepState = 'complete' | 'current' | 'pending';

export interface TimelineStep {
  readonly label: string;
  readonly date: string;
  readonly state: StepState;
  readonly stateText: string;
}

const STATE_TEXT: Readonly<Record<StepState, string>> = localized(() => ({
  complete: t('invoices.publicInvoiceFormat.completed'),
  current: t('invoices.publicInvoiceFormat.currentStep'),
  pending: t('invoices.publicInvoiceFormat.notReachedYet'),
}));

/** BR-27 timeline: a step with a date is complete, the first without one is current. */
export function timelineSteps(invoice: PublicInvoice): readonly TimelineStep[] {
  const { timeline } = invoice;
  const steps: readonly [string, string | null][] = [
    [t('invoices.publicInvoiceFormat.serviceCompleted'), timeline.serviceCompletedOn],
    [t('invoices.publicInvoiceFormat.invoiceSent'), timeline.sentOn],
    [t('invoices.publicInvoiceFormat.payment'), timeline.paidOn],
    [t('invoices.publicInvoiceFormat.receipt'), timeline.receiptOn],
  ];
  let currentTaken = false;
  return steps.map(([label, date]) => {
    let state: StepState = 'complete';
    if (date === null) {
      state = currentTaken ? 'pending' : 'current';
      currentTaken = true;
    }
    return {
      label,
      date: date === null ? t('invoices.publicInvoiceFormat.pending') : formatCalendarDate(date),
      state,
      stateText: STATE_TEXT[state],
    };
  });
}

/** BR-27 chip: "Overdue" replaces the stored status while a balance is late. */
export function statusChip(invoice: PublicInvoice): { label: string; icon: string; tone: string } {
  return STATUS_CHIPS[invoice.overdue && invoice.status !== 'paid' ? 'overdue' : invoice.status];
}

const DAY_MS = 86_400_000;

/** "Payment due in <n> days" / "Due today" / "<n> days overdue" (BR-27). */
export function dueText(invoice: PublicInvoice, now: Date = new Date()): string {
  if (invoice.overdue) {
    const days = invoice.daysOverdue ?? 0;
    return t('invoices.publicInvoiceFormat.overdue', {
      days: days,
      value:
        days === 1 ? t('invoices.publicInvoiceFormat.day') : t('invoices.publicInvoiceFormat.days'),
    });
  }
  const toUtc = (value: string): number => Date.parse(`${value}T00:00:00Z`);
  const days = Math.round(
    (toUtc(invoice.dueDate) - toUtc(todayIn(invoice.timezone, now))) / DAY_MS,
  );
  return days <= 0
    ? t('invoices.publicInvoiceFormat.dueToday')
    : t('invoices.publicInvoiceFormat.paymentDueIn', {
        days: days,
        value:
          days === 1
            ? t('invoices.publicInvoiceFormat.day')
            : t('invoices.publicInvoiceFormat.days'),
      });
}

/** Gross amount minus refunds: what the customer actually paid (BR-31). */
export function netAmount(payment: PublicPayment): number {
  return Math.round((payment.amount - payment.refundedAmount) * 100) / 100;
}

export function netText(payment: PublicPayment, currency: string): string {
  return money(netAmount(payment), currency);
}

/** Card brand of "<Brand> ending in <last4>", `null` for the generic or non-card labels. */
export function cardBrand(methodLabel: string): string | null {
  const brand = /^(\S+) ending in \d{4}$/.exec(methodLabel)?.[1];
  return brand === undefined || brand === 'Card' ? null : brand.toUpperCase();
}

/** Balance in minor units for Stripe (BR-29). */
export function minorUnits(amount: number): number {
  return Math.round(amount * 100);
}

export { formatCalendarDate, money };
