import { QueueResponse } from '../../billing-review/models/billing-review.model';
import {
  InvoiceRow,
  InvoicesOptions,
  OverviewResponse,
  Page,
  PaymentRow,
  RecordPaymentResult,
} from '../models/invoices-hub.model';

/** Contract-shaped bodies shared by the hub specs (specs/invoices-payments-management/spec.md). */
export const optionsBody = (overrides: Partial<InvoicesOptions> = {}): InvoicesOptions => ({
  timezone: 'America/Chicago',
  currency: 'USD',
  paymentPrefix: 'PAY',
  branches: [
    { id: 'b-1', name: 'Austin' },
    { id: 'b-2', name: 'Dallas' },
  ],
  members: [
    { userId: 'u-1', name: 'Alex Morgan' },
    { userId: 'u-2', name: 'Dana Kim' },
  ],
  canAct: true,
  ...overrides,
});

export const invoiceRow = (overrides: Partial<InvoiceRow> = {}): InvoiceRow => ({
  id: 'inv-1',
  number: 'INV-1049',
  customerId: 'c-1',
  customerName: 'Daniel Kim',
  workOrderId: 'wo-1',
  workOrderNumber: 'WO-1042',
  branchName: 'Austin',
  issueDate: '2026-09-19',
  dueDate: '2026-10-03',
  total: 149,
  balanceDue: 149,
  currency: 'USD',
  status: 'sent',
  storedStatus: 'sent',
  daysOverdue: null,
  lastActivity: { kind: 'sent', date: '2026-09-19' },
  hasRecipient: true,
  updatedAt: '2026-09-19T15:00:00Z',
  canRecordPayment: true,
  ...overrides,
});

export const OVERDUE_ROW = invoiceRow({
  id: 'inv-2',
  number: 'INV-1045',
  customerName: 'Jennifer Walsh',
  workOrderId: 'wo-2',
  workOrderNumber: 'WO-1037',
  status: 'overdue',
  daysOverdue: 4,
  total: 325,
  balanceDue: 325,
  canRecordPayment: true,
});
export const PAID_ROW = invoiceRow({
  id: 'inv-3',
  number: 'INV-1048',
  customerName: 'Sofia Martinez',
  workOrderId: 'wo-3',
  workOrderNumber: 'WO-1048',
  status: 'paid',
  storedStatus: 'paid',
  total: 357.28,
  balanceDue: 0,
  lastActivity: { kind: 'paid', date: '2026-09-20' },
  canRecordPayment: false,
});
export const DRAFT_ROW = invoiceRow({
  id: 'inv-4',
  number: 'INV-1050',
  customerName: 'Robert Chen',
  workOrderId: 'wo-4',
  workOrderNumber: 'WO-1053',
  status: 'draft',
  storedStatus: 'draft',
  lastActivity: null,
  canRecordPayment: false,
});

export const pageBody = <T>(items: readonly T[], total = items.length, page = 1): Page<T> => ({
  items,
  page,
  pageSize: 20,
  total,
});

export const paymentRow = (overrides: Partial<PaymentRow> = {}): PaymentRow => ({
  id: 'pay-1',
  number: 'PAY-12',
  paidDate: '2026-09-20',
  customerName: 'Sofia Martinez',
  invoiceId: 'inv-3',
  invoiceNumber: 'INV-1048',
  method: 'card_external',
  reference: null,
  amount: 357.28,
  currency: 'USD',
  receivedByName: 'Alex Morgan',
  ...overrides,
});

export const overviewBody = (): OverviewResponse => ({
  metrics: {
    outstanding: 18760,
    overdue: 4320,
    draft: 2845,
    paidThisMonth: 118140,
    averageDaysToPay: 8.4,
    currency: 'USD',
  },
  aging: { current: 9420, days1To30: 5610, days31To60: 2800, days60Plus: 930 },
  recentPayments: [
    {
      paymentId: 'pay-1',
      paidDate: '2026-09-20',
      customerName: 'Sofia Martinez',
      invoiceId: 'inv-3',
      invoiceNumber: 'INV-1048',
      amount: 357.28,
      method: 'card_external',
    },
  ],
});

export const readyBody = (ready = 6, items: QueueResponse['items'] = []): QueueResponse => ({
  items,
  page: 1,
  pageSize: 20,
  total: items.length,
  tabs: { all: 9, variances: 3, ready },
  metrics: {
    needsReview: 9,
    withVariances: 3,
    readyToInvoice: ready,
    completedValue: 0,
    currency: 'USD',
  },
});

export const resultBody = (overrides: Partial<RecordPaymentResult> = {}): RecordPaymentResult => ({
  changed: true,
  emailStatus: 'sent',
  payment: paymentRow({ id: 'pay-7', number: 'PAY-7', invoiceId: 'inv-1' }),
  invoice: {
    id: 'inv-1',
    status: 'paid',
    amountPaid: 149,
    balanceDue: 0,
    updatedAt: '2026-10-09T15:00:00Z',
  },
  ...overrides,
});
