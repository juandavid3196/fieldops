import { t } from '../../../core/i18n/translate';
/** Customer-facing quote of a token (customer-quote-approval API contracts); never internal data. */
export type PublicQuoteStatus =
  | 'sent'
  | 'clarification_requested'
  | 'approved'
  | 'rejected'
  /** Portal only: the valid-until date passed; read-only, never written by the portal. */
  | 'expired';

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
export const UNAVAILABLE_TITLE = (): string => t('quotes.publicQuoteModel.unavailableTitle');
export const UNAVAILABLE_BODY = (): string => t('quotes.publicQuoteModel.unavailableBody');
export const SUPERSEDED_TITLE = (): string => t('quotes.publicQuoteModel.supersededTitle');
export const SUPERSEDED_BODY = (): string => t('quotes.publicQuoteModel.supersededBody');
export const PUBLIC_LOAD_ERROR = (): string => t('quotes.publicQuoteModel.publicLoadError');
export const PUBLIC_RATE_LIMITED = (): string => t('quotes.publicQuoteModel.publicRateLimited');
export const CALCULATE_ERROR = (): string => t('quotes.publicQuoteModel.calculateError');
export const RESPONSE_ERROR = (): string => t('quotes.publicQuoteModel.responseError');
export const PDF_ERROR = (): string => t('quotes.publicQuoteModel.pdfError');
export const ALREADY_ANSWERED = (): string => t('quotes.publicQuoteModel.alreadyAnswered');
export const APPROVE_HINT = (): string => t('quotes.publicQuoteModel.approveHint');
export const OPTIONAL_SELECTION_ERROR = (): string =>
  t('quotes.publicQuoteModel.optionalSelectionError');
export const ACCEPT_TERMS_ERROR = (): string => t('quotes.publicQuoteModel.acceptTermsError');
export const QUOTE_EXPIRED_NOTICE = (organizationName: string): string =>
  t('quotes.publicQuoteModel.thisQuoteHasExpired', { organizationName: organizationName });
