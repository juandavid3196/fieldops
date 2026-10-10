import { PublicInvoice, PublicPayment } from '../models/invoice.model';
import { STATUS_CHIPS, formatCalendarDate, money, todayIn } from './invoices-hub-format';

export type StepState = 'complete' | 'current' | 'pending';

export interface TimelineStep {
  readonly label: string;
  readonly date: string;
  readonly state: StepState;
  readonly stateText: string;
}

const STATE_TEXT: Readonly<Record<StepState, string>> = {
  complete: 'Completed',
  current: 'Current step',
  pending: 'Not reached yet',
};

/** BR-27 timeline: a step with a date is complete, the first without one is current. */
export function timelineSteps(invoice: PublicInvoice): readonly TimelineStep[] {
  const { timeline } = invoice;
  const steps: readonly [string, string | null][] = [
    ['Service completed', timeline.serviceCompletedOn],
    ['Invoice sent', timeline.sentOn],
    ['Payment', timeline.paidOn],
    ['Receipt', timeline.receiptOn],
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
      date: date === null ? 'Pending' : formatCalendarDate(date),
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
    return `${days} ${days === 1 ? 'day' : 'days'} overdue`;
  }
  const toUtc = (value: string): number => Date.parse(`${value}T00:00:00Z`);
  const days = Math.round(
    (toUtc(invoice.dueDate) - toUtc(todayIn(invoice.timezone, now))) / DAY_MS,
  );
  return days <= 0 ? 'Due today' : `Payment due in ${days} ${days === 1 ? 'day' : 'days'}`;
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
