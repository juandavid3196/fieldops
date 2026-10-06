import { WorkOrderStatus } from '../../jobs/models/work-order.model';
import { CompletedAssessment } from '../../requests/models/requests.model';

export type LineType = 'service' | 'product';
export type TermsPreset = 'due_on_completion' | 'net_15' | 'net_30' | 'custom';
export type QuoteStatus =
  'draft' | 'sent' | 'clarification_requested' | 'approved' | 'rejected' | 'expired' | 'cancelled';
export type EmailStatus = 'sent' | 'failed';

export const QUOTE_ID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** Fixed copy (backend titles are never displayed). */
export const QUOTE_CHANGED_MESSAGE = 'This quote changed. Refresh to see the latest.';
export const QUOTE_SAVE_FAILED_MESSAGE = "We couldn't save this quote. Please try again.";
export const QUOTE_UNAVAILABLE_MESSAGE = "This quote isn't available.";
export const QUOTE_LOAD_ERROR_MESSAGE = "We couldn't load this quote.";
export const EDITOR_FORBIDDEN_MESSAGE = "You don't have access to edit quotes.";
export const VIEW_FORBIDDEN_MESSAGE = "You don't have access to quotes.";
export const CALC_ERROR_MESSAGE = "We couldn't update the totals.";
export const PREVIEW_ERROR_MESSAGE = "We couldn't build the preview.";
export const EMAIL_FAILED_MESSAGE =
  "Quote sent, but we couldn't email it. Use Resend email to try again.";
export const NO_RECIPIENT_MESSAGE = 'This customer has no email address.';
export const SMS_UNAVAILABLE_MESSAGE = "SMS isn't available yet.";

export const TERMS_PRESETS: { code: TermsPreset; label: string }[] = [
  { code: 'due_on_completion', label: 'Payment due upon completion' },
  { code: 'net_15', label: 'Net 15' },
  { code: 'net_30', label: 'Net 30' },
  { code: 'custom', label: 'Custom' },
];

/** Frozen texts of the presets (BR-18). */
export const TERMS_TEXTS: Readonly<Record<Exclude<TermsPreset, 'custom'>, string>> = {
  due_on_completion: 'Payment due upon completion.',
  net_15: 'Payment due within 15 days of the invoice date.',
  net_30: 'Payment due within 30 days of the invoice date.',
};

export interface DraftLine {
  readonly catalogItemId: string | null;
  readonly type: LineType;
  readonly name: string;
  readonly description: string;
  readonly quantity: number;
  readonly unit: string;
  readonly unitPrice: number;
  readonly unitCost: number;
  readonly taxable: boolean;
  readonly isOptional: boolean;
}

export interface TermsBody {
  readonly preset: TermsPreset;
  readonly customText?: string;
}

export interface DraftBody {
  readonly lines: readonly DraftLine[];
  readonly discountTotal: number;
  readonly customerMessage: string | null;
  readonly internalNote: string | null;
  readonly terms: TermsBody;
  readonly validUntil: string;
}

export interface CalculatedLine {
  readonly lineSubtotal: number;
  readonly discountShare: number;
  readonly taxRate: number;
  readonly lineTax: number;
  readonly lineTotal: number;
}

export interface Margin {
  readonly percent: number | null;
  readonly grossProfit: number;
}

export interface Calculation {
  readonly lines: readonly CalculatedLine[];
  readonly subtotal: number;
  readonly discountTotal: number;
  readonly taxLabel: string;
  readonly taxTotal: number;
  readonly total: number;
  readonly currency: string;
  readonly margin: Margin;
}

export interface QuoteDraft extends DraftBody {
  readonly versionNo: number;
  readonly calculation: Calculation;
}

export interface SentVersionSummary {
  readonly versionNo: number;
  readonly sentAt: string;
  readonly total: number;
  readonly isCurrent: boolean;
}

export type QuoteResponseType = 'approved' | 'rejected' | 'clarification_requested';

/** A customer response of a sent version (BR-23), newest first in `QuoteDetail.responses`. */
export interface QuoteResponse {
  readonly versionNo: number;
  readonly type: QuoteResponseType;
  readonly respondedAt: string;
  readonly responderName: string;
  readonly comment: string | null;
  readonly selectedOptionalLines: readonly {
    readonly name: string;
    readonly lineSubtotal: number;
  }[];
  readonly totals: {
    readonly subtotal: number;
    readonly discountTotal: number;
    readonly taxTotal: number;
    readonly total: number;
  } | null;
}

export interface QuoteDetail {
  readonly id: string;
  readonly number: number;
  readonly displayNumber: string;
  readonly status: QuoteStatus;
  /** `expired` when a sent quote is past its validity, else `status` (BR-23). */
  readonly displayStatus: QuoteStatus;
  readonly responses: readonly QuoteResponse[];
  readonly updatedAt: string;
  readonly canManage: boolean;
  /** The work order of the approved version, if any (create-work-order BR-04). */
  readonly workOrder: {
    readonly id: string;
    readonly displayNumber: string;
    readonly status: WorkOrderStatus;
  } | null;
  readonly canManageWorkOrders: boolean;
  readonly request: {
    readonly id: string;
    readonly displayNumber: string;
    readonly title: string;
    readonly category: string | null;
    readonly status: string;
  };
  readonly customer: {
    readonly id: string | null;
    readonly name: string;
    readonly phone: string | null;
    readonly email: string | null;
    readonly address: string | null;
  };
  readonly recipient: { readonly email: string | null };
  readonly completedAssessment: CompletedAssessment | null;
  readonly organization: {
    readonly name: string;
    readonly currency: string;
    readonly defaultTaxRate: number;
  };
  readonly draft: QuoteDraft | null;
  readonly sentVersions: readonly SentVersionSummary[];
}

export interface VersionLine {
  readonly type: LineType;
  readonly name: string;
  readonly description: string;
  readonly quantity: number;
  readonly unit: string;
  readonly unitPrice: number;
  readonly unitCost: number;
  readonly taxRate: number;
  readonly lineSubtotal: number;
  readonly lineTax: number;
  readonly lineTotal: number;
  readonly isOptional: boolean;
}

/** A frozen sent version; `terms` is the stored text (a draft-style object is also accepted). */
export interface QuoteVersion {
  readonly versionNo: number;
  readonly sentAt: string;
  readonly scope: string;
  readonly customerMessage: string | null;
  readonly internalNote: string | null;
  readonly terms: string | TermsBody | null;
  readonly validUntil: string;
  readonly currency: string;
  readonly lines: readonly VersionLine[];
  readonly subtotal: number;
  readonly discountTotal: number;
  readonly taxLabel: string;
  readonly taxTotal: number;
  readonly total: number;
  readonly margin: Margin;
}

export interface SendResult {
  readonly quote: QuoteDetail;
  readonly emailStatus: EmailStatus;
}

export interface DiscardResult {
  readonly quoteId: string;
  readonly status: QuoteStatus;
  readonly requestId: string;
}

/** Customer-visible content of the preview and of a frozen version (BR-23); never internal data. */
export interface CustomerViewLine {
  readonly name: string;
  readonly description: string;
  readonly quantity: number;
  readonly unit: string;
  readonly unitPrice: number;
  readonly amount: number;
}

export interface CustomerView {
  readonly organizationName: string;
  readonly number: string;
  readonly versionNo: number;
  readonly date: string;
  readonly customerName: string;
  readonly address: string | null;
  readonly scope: string;
  readonly message: string | null;
  readonly lines: readonly CustomerViewLine[];
  readonly optionalLines: readonly CustomerViewLine[];
  readonly subtotal: number;
  readonly discountTotal: number;
  readonly taxLabel: string;
  readonly taxTotal: number;
  readonly total: number;
  readonly currency: string;
  readonly terms: string;
  readonly validUntil: string;
}
