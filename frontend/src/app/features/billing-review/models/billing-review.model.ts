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
export const READ_ONLY_MESSAGE = 'Only owners and accounting can review and generate invoices.';
export const FORBIDDEN_MESSAGE = "You don't have access to the completed jobs review.";
export const QUEUE_ERROR_MESSAGE = "We couldn't load completed jobs. Try again.";
export const QUEUE_EMPTY_MESSAGE = 'No completed jobs need review.';
export const QUEUE_FILTERED_EMPTY_MESSAGE = 'No jobs match these filters.';
export const DETAIL_ERROR_MESSAGE = "We couldn't load this job. Try again.";
export const DETAIL_GONE_MESSAGE = 'This job is no longer in the review queue.';
export const ACTION_FAILED_MESSAGE = "We couldn't complete this action. Try again.";
export const EXPORT_FAILED_MESSAGE = "We couldn't export the queue. Try again.";
export const EXPORT_TOO_LARGE_MESSAGE = 'Narrow the filters to export 5,000 jobs or fewer.';
export const NOTE_TOO_LONG_MESSAGE = 'Use 500 characters or fewer.';
export const ISSUE_DATE_MESSAGE = 'Choose an issue date within the last 30 days.';
export const TERMS_MESSAGE = 'Select payment terms.';
/** Fixed copy for the `409` codes of the generate endpoint (backend titles are never displayed). */
export const GENERATE_MESSAGES: Readonly<Record<string, string>> = {
  work_order_status_invalid: 'This job is no longer ready for invoicing.',
  completion_requirements_unmet:
    'Complete the required closing items before generating an invoice. This job was marked for follow-up.',
  variance_confirmation_required: 'Confirm the variances to generate this invoice.',
  invoice_totals_mismatch:
    "The approved quote amounts don't match its lines. Review the quote before invoicing.",
};
