/** Customer-facing quote of a token (customer-quote-approval API contracts); never internal data. */
export type PublicQuoteStatus = 'sent' | 'clarification_requested' | 'approved' | 'rejected';

export interface PublicTotals {
  readonly subtotal: number;
  readonly discountTotal: number;
  readonly taxLabel: string;
  readonly taxTotal: number;
  readonly total: number;
  readonly currency: string;
}

export interface PublicLine {
  /** Non-null only for optional lines. */
  readonly id: string | null;
  readonly name: string;
  readonly description: string;
  readonly quantity: number;
  readonly unit: string;
  readonly unitPrice: number;
  readonly lineSubtotal: number;
  readonly isOptional: boolean;
}

export interface PublicQuote {
  readonly organization: {
    readonly name: string;
    readonly phone: string | null;
    readonly hasLogo: boolean;
  };
  readonly quote: {
    readonly displayNumber: string;
    readonly versionNo: number;
    readonly status: PublicQuoteStatus;
    readonly sentOn: string;
    readonly validUntil: string;
    readonly scope: string;
    readonly customerMessage: string | null;
    readonly terms: string | null;
  };
  readonly customer: { readonly name: string; readonly address: string | null };
  readonly scopeItems: readonly string[];
  readonly lines: readonly PublicLine[];
  readonly versionTotals: PublicTotals;
  readonly photos: readonly { readonly id: string }[];
  readonly progress: {
    readonly requestSubmittedOn: string;
    readonly assessmentCompletedOn: string | null;
  };
  readonly clarification: { readonly askedOn: string } | null;
  readonly response: {
    readonly type: 'approved' | 'rejected';
    readonly respondedOn: string;
    readonly selectedOptionalLineIds: readonly string[];
    readonly totals: PublicTotals | null;
  } | null;
}

/** Fixed copy (backend titles are never displayed). */
export const UNAVAILABLE_TITLE = "This link isn't available.";
export const UNAVAILABLE_BODY =
  "This quote link isn't available. It may have expired or been replaced. Please contact the company that sent it.";
export const SUPERSEDED_TITLE = 'A newer version of this quote is available.';
export const SUPERSEDED_BODY = 'Check your email for the most recent link from the company.';
export const PUBLIC_LOAD_ERROR = "We couldn't load this quote.";
export const PUBLIC_RATE_LIMITED = 'Too many attempts. Please wait a few minutes and try again.';
export const CALCULATE_ERROR = "We couldn't update the total. Please try again.";
export const RESPONSE_ERROR = "We couldn't send your response. Please try again.";
export const PDF_ERROR = "We couldn't download the PDF. Please try again.";
export const ALREADY_ANSWERED = 'This quote has already been answered.';
export const APPROVE_HINT = 'Check the box to approve.';
export const OPTIONAL_SELECTION_ERROR = "One or more optional items aren't available.";
export const ACCEPT_TERMS_ERROR =
  'Confirm that you approve the scope of work and agree to the terms.';
