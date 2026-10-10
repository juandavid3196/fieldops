export const HUB_TABS = ['invoices', 'payments', 'ready'] as const;
export type HubTab = (typeof HUB_TABS)[number];

/** BR-07 `status` filter values (display statuses) and the Invoices tab status filter. */
export const STATUS_FILTERS = ['draft', 'sent', 'partially_paid', 'paid', 'overdue'] as const;
export type StatusFilter = (typeof STATUS_FILTERS)[number];

export const PAYMENT_METHODS = [
  'cash',
  'bank_transfer',
  'card_external',
  'check',
  'other',
  'card_online',
] as const;
export type PaymentMethod = (typeof PAYMENT_METHODS)[number];

/** Methods an internal user can record (BR-31 c): `card_online` only arises from the public page. */
export const RECORDABLE_METHODS: readonly PaymentMethod[] = [
  'cash',
  'bank_transfer',
  'card_external',
  'check',
  'other',
];

/** Payment field rules: labels. */
export const PAYMENT_METHOD_LABELS: Readonly<Record<PaymentMethod, string>> = {
  cash: 'Cash',
  bank_transfer: 'Bank transfer',
  card_external: 'External card payment',
  check: 'Check',
  other: 'Other',
  card_online: 'Online card',
};

/** Payment field rules: methods whose reference is required. */
export const REFERENCE_REQUIRED_METHODS: readonly PaymentMethod[] = [
  'card_external',
  'bank_transfer',
  'check',
];

/** Load state of one region of the hub (own skeleton, error and Retry). */
export interface Region<T> {
  readonly status: 'idle' | 'loading' | 'ready' | 'error';
  readonly data: T | null;
}

export const IDLE_REGION: Region<never> = { status: 'idle', data: null };

export interface NamedOption {
  readonly id: string;
  readonly name: string;
}

export interface MemberOption {
  readonly userId: string;
  readonly name: string;
}

/** `GET /invoices/options` (BR-19). */
export interface InvoicesOptions {
  readonly timezone: string;
  readonly currency: string;
  readonly paymentPrefix: string;
  readonly branches: readonly NamedOption[];
  readonly members: readonly MemberOption[];
  readonly canAct: boolean;
}

export interface RecentPayment {
  readonly paymentId: string;
  readonly paidDate: string;
  readonly customerName: string;
  readonly invoiceId: string;
  readonly invoiceNumber: string;
  readonly amount: number;
  readonly method: PaymentMethod;
}

/** `GET /invoices/overview`. */
export interface OverviewResponse {
  readonly metrics: {
    readonly outstanding: number;
    readonly overdue: number;
    readonly draft: number;
    readonly paidThisMonth: number;
    readonly averageDaysToPay: number | null;
    readonly currency: string;
  };
  readonly aging: {
    readonly current: number;
    readonly days1To30: number;
    readonly days31To60: number;
    readonly days60Plus: number;
  };
  readonly recentPayments: readonly RecentPayment[];
}

export type StoredStatus = 'draft' | 'sent' | 'partially_paid' | 'paid';
export type DisplayStatus = StoredStatus | 'overdue';

export interface LastActivity {
  readonly kind: 'paid' | 'payment' | 'sent';
  readonly date: string;
}

export interface InvoiceRow {
  readonly id: string;
  readonly number: string;
  readonly customerId: string;
  readonly customerName: string;
  readonly workOrderId: string;
  readonly workOrderNumber: string;
  readonly branchName: string;
  readonly issueDate: string;
  readonly dueDate: string;
  readonly total: number;
  readonly balanceDue: number;
  readonly currency: string;
  readonly status: DisplayStatus;
  readonly storedStatus: StoredStatus;
  readonly daysOverdue: number | null;
  readonly lastActivity: LastActivity | null;
  readonly hasRecipient: boolean;
  readonly updatedAt: string;
  readonly canRecordPayment: boolean;
}

export type PaymentStatus = 'succeeded' | 'partially_refunded' | 'refunded';

/** Text tags for refunded payments (BR-31 b); `null` for a plain succeeded payment. */
export const PAYMENT_STATUS_LABELS: Readonly<Record<PaymentStatus, string | null>> = {
  succeeded: null,
  partially_refunded: 'Partially refunded',
  refunded: 'Refunded',
};

export interface PaymentRow {
  readonly id: string;
  readonly number: string;
  readonly paidDate: string;
  readonly customerName: string;
  readonly invoiceId: string;
  readonly invoiceNumber: string;
  readonly method: PaymentMethod;
  readonly reference: string | null;
  /** Gross amount; the list shows `amount − refundedAmount` (BR-31). */
  readonly amount: number;
  readonly currency: string;
  readonly status: PaymentStatus;
  readonly refundedAmount: number;
  /** `null` for online payments, shown "—". */
  readonly receivedByName: string | null;
}

export interface Page<T> {
  readonly items: readonly T[];
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface InvoiceFilters {
  readonly search: string;
  readonly from: string;
  readonly to: string;
  readonly status: StatusFilter | null;
  readonly customerId: string | null;
  readonly branchId: string | null;
}

export interface PaymentFilters {
  readonly search: string;
  readonly from: string;
  readonly to: string;
  readonly method: PaymentMethod | null;
  readonly branchId: string | null;
}

export interface RecordPaymentBody {
  readonly idempotencyKey: string;
  readonly amount: number;
  readonly paidDate: string;
  readonly method: PaymentMethod;
  readonly reference: string | null;
  readonly receivedByUserId: string;
  readonly sendReceipt: boolean;
  readonly note: string | null;
  readonly updatedAt: string;
}

export interface RecordPaymentResult {
  readonly changed: boolean;
  readonly emailStatus: 'sent' | 'failed' | 'not_sent';
  readonly payment: PaymentRow;
  readonly invoice: {
    readonly id: string;
    readonly status: StoredStatus;
    readonly amountPaid: number;
    readonly balanceDue: number;
    readonly updatedAt: string;
  };
}

export const HUB_FORBIDDEN_MESSAGE = "You don't have access to invoices.";
export const INVOICES_ERROR_MESSAGE = "We couldn't load invoices. Try again.";
export const PAYMENTS_ERROR_MESSAGE = "We couldn't load payments. Try again.";
export const READY_ERROR_MESSAGE = "We couldn't load completed jobs. Try again.";
export const METRICS_ERROR_MESSAGE = "We couldn't load billing metrics. Try again.";
export const OPTIONS_ERROR_MESSAGE = "We couldn't load payment options. Try again.";
export const CUSTOMERS_ERROR_MESSAGE = "We couldn't load customers.";
export const NO_CUSTOMERS_MESSAGE = 'No customers found.';
export const INVOICES_EMPTY_MESSAGE = 'No invoices yet. Generate invoices from completed jobs.';
export const INVOICES_FILTERED_EMPTY_MESSAGE = 'No invoices match these filters.';
export const PAYMENTS_EMPTY_MESSAGE = 'No payments recorded yet.';
export const PAYMENTS_FILTERED_EMPTY_MESSAGE = 'No payments match these filters.';
export const READY_EMPTY_MESSAGE =
  'No completed jobs are ready to invoice. Jobs with variances or missing requirements stay in Needs review.';
export const EXPORT_FAILED_MESSAGE = "We couldn't export. Try again.";
export const EXPORT_TOO_LARGE_MESSAGE = 'Narrow the filters to export 5,000 rows or fewer.';
export const PDF_FAILED_MESSAGE = "We couldn't download the PDF. Try again.";

export const PAYMENT_FAILED_MESSAGE = "We couldn't record this payment. Try again.";
export const PAYMENT_FORBIDDEN_MESSAGE = 'Only owners and accounting can record payments.';
export const PAYMENT_UNAVAILABLE_MESSAGE = "This invoice isn't available.";
export const PAYMENT_ALREADY_RECORDED_MESSAGE = 'This payment was already recorded.';
export const RECEIPT_SENT_MESSAGE = 'Receipt sent to the customer.';
export const RECEIPT_FAILED_MESSAGE = "Payment recorded, but we couldn't email the receipt.";
export const PAYMENT_CONFLICT_MESSAGES: Readonly<Record<string, string>> = {
  invoice_changed: 'This invoice changed. Refresh to see the latest.',
  invoice_not_payable: "This invoice can't receive payments.",
  payment_in_progress:
    'An online card payment is in progress for this invoice. Try again in a few minutes.',
};
export const NO_RECIPIENT_MESSAGE = 'This invoice has no recipient email.';
export const NOTE_MAX_LENGTH = 500;
export const REFERENCE_MAX_LENGTH = 160;

/** Query-param state of the hub (BR-20): an invalid value falls back to the default. */
export interface HubQuery {
  readonly tab: HubTab;
  readonly status: StatusFilter | null;
}

export function resolveHubQuery(tab: string | null, status: string | null): HubQuery {
  const resolvedTab = HUB_TABS.find((code) => code === tab) ?? 'invoices';
  const resolvedStatus =
    resolvedTab === 'invoices' ? (STATUS_FILTERS.find((code) => code === status) ?? null) : null;
  return { tab: resolvedTab, status: resolvedStatus };
}
