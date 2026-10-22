import { localized, t } from '../../../core/i18n/translate';
export type CompletedRange = '7d' | '30d' | '90d' | 'all';
export type VarianceFilter = 'all' | 'none' | 'any' | 'labor' | 'material';
export type QueueTab = 'all' | 'variances' | 'ready';
export type LaborVariance = 'none' | 'over' | 'under';
export type PaymentTerms = 'due_upon_receipt' | 'net_15' | 'net_30';
export type LineStatus =
  'matches' | 'over' | 'under' | 'not_in_quote' | 'not_tracked' | 'see_labor_total';
export type VerificationKey =
  'checklist' | 'photos' | 'acknowledgment' | 'materials' | 'labor' | 'summary';
export type EvidenceKind = 'before' | 'during' | 'after' | 'incident' | 'other';

export interface NamedOption {
  readonly id: string;
  readonly name: string;
}

/** `GET /billing-review/options`. */
export interface BillingReviewOptions {
  readonly timezone: string;
  readonly currency: string;
  readonly branches: readonly NamedOption[];
  readonly technicians: readonly NamedOption[];
  readonly canAct: boolean;
}

export interface QueueFilters {
  readonly branchId: string | null;
  readonly completed: CompletedRange;
  readonly technicianId: string | null;
  readonly variance: VarianceFilter;
  readonly search: string;
  readonly tab: QueueTab;
  readonly page: number;
}

export interface QueueItem {
  readonly workOrderId: string;
  readonly number: string;
  readonly title: string;
  readonly customerName: string;
  readonly approvedTotal: number;
  readonly currency: string;
  readonly completedAt: string;
  readonly laborVariance: LaborVariance;
  readonly materialVariance: boolean;
  readonly ready: boolean;
  readonly followUp: boolean;
}

export interface QueueMetrics {
  readonly needsReview: number;
  readonly withVariances: number;
  readonly readyToInvoice: number;
  readonly completedValue: number;
  readonly currency: string;
}

export interface QueueTabCounts {
  readonly all: number;
  readonly variances: number;
  readonly ready: number;
}

/** `GET /billing-review/queue`. */
export interface QueueResponse {
  readonly items: readonly QueueItem[];
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
  readonly tabs: QueueTabCounts;
  readonly metrics: QueueMetrics;
}

export interface ComparisonLine {
  readonly index: number | null;
  readonly kind: 'quote' | 'labor_total' | 'not_in_quote';
  readonly name: string;
  readonly approved: string | null;
  readonly actual: string | null;
  readonly billable: string | null;
  readonly tax: 'taxable' | 'non_taxable' | null;
  readonly amount: number | null;
  readonly status: LineStatus;
}

export interface VerificationItem {
  readonly key: VerificationKey;
  readonly met: boolean;
  readonly mandatory: boolean;
  readonly label: string;
}

export interface ReviewTotals {
  readonly approvedSubtotal: number;
  readonly invoiceSubtotal: number;
  readonly discountTotal: number;
  readonly taxLabel: string;
  readonly taxTotal: number;
  readonly invoiceTotal: number;
  readonly variance: number;
  readonly currency: string;
}

export interface WorkEvidenceVisit {
  readonly visitId: string;
  readonly visitNumber: number;
  readonly checklist: readonly {
    readonly label: string;
    readonly required: boolean;
    readonly completed: boolean;
  }[];
  readonly materials: readonly {
    readonly description: string;
    readonly quantity: string;
    readonly unit: string;
    readonly origin: 'planned' | 'added';
  }[];
  readonly evidence: readonly {
    readonly id: string;
    readonly type: EvidenceKind;
    readonly caption: string | null;
  }[];
  readonly completionSummary: string | null;
  readonly acknowledgment: {
    readonly method: string;
    readonly signerName: string | null;
    readonly relationship: string | null;
    readonly comment: string | null;
    readonly signatureCaptured: boolean;
  } | null;
}

export interface InvoiceDefaults {
  readonly numberPreview: string;
  readonly issueDate: string;
  readonly paymentTerms: PaymentTerms;
  readonly dueDate: string;
  readonly taxLabel: string;
  readonly currency: string;
}

export interface FollowUp {
  readonly at: string;
  readonly byName: string;
}

/** `GET /billing-review/work-orders/{id}`. */
export interface BillingReviewDetail {
  readonly header: {
    readonly workOrderId: string;
    readonly number: string;
    readonly title: string;
    readonly quoteNumber: string;
    readonly customerName: string;
    readonly propertyAddress: string;
    readonly completedByName: string | null;
    readonly completedAt: string;
    readonly timezone: string;
  };
  readonly lines: readonly ComparisonLine[];
  readonly laborVariance: LaborVariance;
  readonly materialVariance: boolean;
  readonly totals: ReviewTotals;
  readonly verification: readonly VerificationItem[];
  readonly ready: boolean;
  readonly evidence: {
    readonly count: number;
    readonly thumbnails: readonly { readonly id: string; readonly type: 'before' | 'after' }[];
  };
  readonly workEvidence: readonly WorkEvidenceVisit[];
  readonly auditTrail: readonly {
    readonly label: string;
    readonly actorName: string;
    readonly occurredAt: string;
  }[];
  readonly invoiceDefaults: InvoiceDefaults;
  readonly note: string | null;
  readonly followUp: FollowUp | null;
  readonly canAct: boolean;
}

export interface ReviewUpdate {
  readonly note?: string;
  readonly followUp?: boolean;
  readonly reason?: 'manual' | 'returned_to_queue';
}

export interface ReviewState {
  readonly note: string | null;
  readonly followUp: FollowUp | null;
}

export interface GenerateInvoiceBody {
  readonly issueDate: string;
  readonly paymentTerms: PaymentTerms;
  readonly note: string;
  readonly acknowledgeVariances: boolean;
}

export interface GeneratedInvoice {
  readonly changed: boolean;
  readonly invoice: {
    readonly id: string;
    readonly number: string;
    readonly status: string;
    readonly total: number;
    readonly currency: string;
  };
}

export const NOTE_MAX_LENGTH = 500;
export const READ_ONLY_MESSAGE = (): string =>
  t('billingReview.billingReviewModel.readOnlyMessage');
export const FORBIDDEN_MESSAGE = (): string =>
  t('billingReview.billingReviewModel.forbiddenMessage');
export const QUEUE_ERROR_MESSAGE = (): string =>
  t('billingReview.billingReviewModel.queueErrorMessage');
export const QUEUE_EMPTY_MESSAGE = (): string =>
  t('billingReview.billingReviewModel.queueEmptyMessage');
export const QUEUE_FILTERED_EMPTY_MESSAGE = (): string =>
  t('billingReview.billingReviewModel.queueFilteredEmptyMessage');
export const DETAIL_ERROR_MESSAGE = (): string =>
  t('billingReview.billingReviewModel.detailErrorMessage');
export const DETAIL_GONE_MESSAGE = (): string =>
  t('billingReview.billingReviewModel.detailGoneMessage');
export const ACTION_FAILED_MESSAGE = (): string =>
  t('billingReview.billingReviewModel.actionFailedMessage');
export const EXPORT_FAILED_MESSAGE = (): string =>
  t('billingReview.billingReviewModel.exportFailedMessage');
export const EXPORT_TOO_LARGE_MESSAGE = (): string =>
  t('billingReview.billingReviewModel.exportTooLargeMessage');
export const NOTE_TOO_LONG_MESSAGE = (): string =>
  t('billingReview.billingReviewModel.noteTooLongMessage');
export const ISSUE_DATE_MESSAGE = (): string =>
  t('billingReview.billingReviewModel.issueDateMessage');
export const TERMS_MESSAGE = (): string => t('billingReview.billingReviewModel.termsMessage');
/** Fixed copy for the `409` codes of the generate endpoint (backend titles are never displayed). */
export const GENERATE_MESSAGES: Readonly<Record<string, string>> = localized(() => ({
  work_order_status_invalid: t('billingReview.billingReviewModel.thisJobIsNo'),
  completion_requirements_unmet: t('billingReview.billingReviewModel.completeTheRequiredClosing'),
  variance_confirmation_required: t('billingReview.billingReviewModel.confirmTheVariancesTo'),
  invoice_totals_mismatch: t('billingReview.billingReviewModel.theApprovedQuoteAmounts'),
}));
