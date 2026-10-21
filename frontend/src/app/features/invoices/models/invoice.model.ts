import { PaymentTerms } from '../../billing-review/models/billing-review.model';

import { localized, t } from '../../../core/i18n/translate';
/** Contracts of specs/invoice-draft-delivery/spec.md (Final). */

export interface InvoiceLine {
  readonly description: string;
  readonly detail: string | null;
  readonly quantity: string;
  readonly unit: string;
  readonly unitPrice: number;
  readonly taxRate: number;
  readonly amount: number;
}

export interface InvoiceTotals {
  readonly subtotal: number;
  readonly discountTotal: number;
  readonly taxLabel: string;
  readonly taxTotal: number;
  readonly total: number;
}

export interface InvoicePreview {
  readonly number: string;
  readonly issueDate: string;
  readonly dueDate: string;
  readonly paymentTerms: PaymentTerms;
  readonly currency: string;
  readonly timezone: string;
  readonly organization: {
    readonly name: string;
    readonly addressLines: readonly string[];
    readonly phone: string | null;
    readonly email: string | null;
    readonly hasLogo: boolean;
  };
  readonly billTo: {
    readonly name: string;
    readonly email: string | null;
    readonly phone: string | null;
    readonly addressLines: readonly string[];
  };
  readonly serviceAddress: string | null;
  readonly workOrderNumber: string;
  readonly lines: readonly InvoiceLine[];
  readonly totals: InvoiceTotals;
  readonly completionNote: string | null;
}

export interface InvoiceCheck {
  readonly key: 'recipient' | 'tax';
  readonly met: boolean;
  readonly label: string;
}

export interface InvoiceDetail extends InvoicePreview {
  readonly id: string;
  /** `draft`, `sent`; other statuses render read-only with their name. */
  readonly status: string;
  readonly workOrderId: string;
  readonly customerName: string;
  readonly delivery: {
    readonly recipientEmail: string | null;
    readonly message: string | null;
    readonly saved: boolean;
  };
  readonly checks: readonly InvoiceCheck[];
  readonly createdAt: string;
  readonly createdByName: string;
  readonly sentAt: string | null;
  readonly canAct: boolean;
  readonly updatedAt: string;
}

/** Contracts of specs/customer-invoice-payments/spec.md (Final), BR-03 to BR-05. */
export interface PublicTimeline {
  readonly serviceCompletedOn: string | null;
  readonly sentOn: string | null;
  readonly paidOn: string | null;
  readonly receiptOn: string | null;
}

export interface PublicService {
  readonly title: string;
  readonly workOrderNumber: string;
  readonly completionNote: string | null;
  readonly technicianName: string | null;
  readonly serviceDate: string | null;
  readonly hasCompletionReport: boolean;
  readonly photoCount: number;
}

export interface PublicBankTransfer {
  readonly available: boolean;
  readonly bankName?: string;
  readonly accountNumber?: string;
  readonly routingNumber?: string;
  readonly reference?: string;
  readonly amount?: number;
}

export interface PublicPaymentOptions {
  readonly card: { readonly available: boolean; readonly publishableKey: string | null };
  readonly bankTransfer: PublicBankTransfer;
  readonly cash: { readonly phone: string | null; readonly email: string | null };
}

export interface PublicPayment {
  readonly paymentId: string;
  readonly number: string;
  readonly receiptNumber: string | null;
  readonly paidOn: string;
  readonly method: string;
  readonly methodLabel: string;
  /** Gross amount; the page shows `amount − refundedAmount`. */
  readonly amount: number;
  readonly refundedAmount: number;
  readonly status: 'succeeded' | 'partially_refunded' | 'refunded';
}

export interface PublicInvoice extends InvoicePreview {
  readonly status: 'sent' | 'partially_paid' | 'paid';
  readonly overdue: boolean;
  readonly daysOverdue: number | null;
  readonly amountPaid: number;
  readonly balanceDue: number;
  readonly customerFirstName: string | null;
  readonly timeline: PublicTimeline;
  readonly service: PublicService;
  readonly paymentOptions: PublicPaymentOptions | null;
  readonly activeAttempt: { readonly attemptId: string; readonly status: 'pending' } | null;
  readonly transferReportedOn: string | null;
  readonly payments: readonly PublicPayment[];
  readonly review: { readonly available: boolean; readonly submitted: boolean };
  readonly receiptEmail: string | null;
}

export interface CardIntent {
  readonly attemptId: string;
  readonly clientSecret: string | null;
  readonly amount: number;
  readonly currency: string;
  readonly status: AttemptStatus;
}

export type AttemptStatus = 'pending' | 'succeeded' | 'failed' | 'partially_refunded' | 'refunded';

export interface AttemptState {
  readonly attemptId: string;
  readonly status: AttemptStatus;
  readonly failureCategory: string | null;
  readonly payment: PublicPayment | null;
  readonly invoice: {
    readonly status: PublicInvoice['status'];
    readonly amountPaid: number;
    readonly balanceDue: number;
  };
}

export interface BankTransferNotice {
  readonly changed: boolean;
  readonly reportedOn: string;
}

export interface PublicPhoto {
  readonly photoId: string;
  readonly type: 'before' | 'after';
  readonly caption: string | null;
  readonly takenOn: string;
}

export type EmailStatus = 'sent' | 'failed' | 'not_sent';

export interface SendResult {
  readonly changed: boolean;
  readonly emailStatus: EmailStatus;
  readonly invoice: InvoiceDetail;
}

export interface ResendResult {
  readonly emailStatus: 'sent' | 'failed';
  readonly invoice: InvoiceDetail;
}

export interface DeliveryValues {
  readonly paymentTerms: PaymentTerms | null;
  readonly recipientEmail: string;
  readonly message: string;
}

export interface DeliveryBody {
  readonly paymentTerms: PaymentTerms;
  readonly recipientEmail: string;
  readonly message: string;
  readonly updatedAt: string;
}

export type DeliveryField = 'recipientEmail' | 'paymentTerms' | 'message';
export type DeliveryErrors = Partial<Record<DeliveryField, string>>;

export interface PdfFile {
  readonly blob: Blob;
  readonly fileName: string;
}

export const INVOICE_READ_ROLES: readonly string[] = [
  'owner',
  'operations_manager',
  'dispatcher',
  'accounting',
  'viewer',
];

export const MESSAGE_MAX_LENGTH = 500;
export const EMAIL_MAX_LENGTH = 254;

export const FORBIDDEN_MESSAGE = (): string => t('invoices.invoiceModel.forbiddenMessage');
export const UNAVAILABLE_MESSAGE = (): string => t('invoices.invoiceModel.unavailableMessage');
export const LOAD_ERROR_MESSAGE = (): string => t('invoices.invoiceModel.loadErrorMessage');
export const READ_ONLY_MESSAGE = (): string => t('invoices.invoiceModel.readOnlyMessage');
export const ACTION_FAILED_MESSAGE = (): string => t('invoices.invoiceModel.actionFailedMessage');
export const PDF_FAILED_MESSAGE = (): string => t('invoices.invoiceModel.pdfFailedMessage');
export const SMS_UNAVAILABLE_MESSAGE = (): string =>
  t('invoices.invoiceModel.smsUnavailableMessage');
export const LATER_MESSAGE = (): string => t('invoices.invoiceModel.laterMessage');
export const EMAIL_FAILED_MESSAGE = (): string => t('invoices.invoiceModel.emailFailedMessage');
export const TERMS_MESSAGE = (): string => t('invoices.invoiceModel.termsMessage');
export const RECIPIENT_REQUIRED_MESSAGE = (): string =>
  t('invoices.invoiceModel.recipientRequiredMessage');
export const RECIPIENT_INVALID_MESSAGE = (): string =>
  t('invoices.invoiceModel.recipientInvalidMessage');
export const MESSAGE_REQUIRED_MESSAGE = (): string =>
  t('invoices.invoiceModel.messageRequiredMessage');
export const MESSAGE_TOO_LONG_MESSAGE = (): string =>
  t('invoices.invoiceModel.messageTooLongMessage');

/** Fixed copy for the `409` codes (backend titles are never displayed). */
export const CONFLICT_MESSAGES: Readonly<Record<string, string>> = localized(() => ({
  invoice_changed: t('invoices.invoiceModel.thisInvoiceChangedRefresh'),
  invoice_not_draft: t('invoices.invoiceModel.thisInvoiceHasAlready'),
  invoice_not_sent: t('invoices.invoiceModel.onlySentInvoicesCan'),
}));

/** BR-34 neutral state. */
export const PUBLIC_UNAVAILABLE_MESSAGE = (): string =>
  t('invoices.invoiceModel.publicUnavailableMessage');
export const PUBLIC_UNAVAILABLE_DETAIL = (): string =>
  t('invoices.invoiceModel.publicUnavailableDetail');
export const PUBLIC_PROTECTION_MESSAGE = (): string =>
  t('invoices.invoiceModel.publicProtectionMessage');
export const PUBLIC_RATE_LIMITED_MESSAGE = (): string =>
  t('invoices.invoiceModel.publicRateLimitedMessage');
export const PUBLIC_DOWNLOAD_FAILED_MESSAGE = (): string =>
  t('invoices.invoiceModel.publicDownloadFailedMessage');

/** BR-29 card flow copy. */
export const CARD_FAILURE_TITLE = (): string => t('invoices.invoiceModel.cardFailureTitle');
export const CARD_DECLINED_MESSAGE = (): string => t('invoices.invoiceModel.cardDeclinedMessage');
export const CARD_EXPIRED_MESSAGE = (): string => t('invoices.invoiceModel.cardExpiredMessage');
export const CARD_OTHER_FAILURE_MESSAGE = (): string =>
  t('invoices.invoiceModel.cardOtherFailureMessage');
export const CARD_STILL_CONFIRMING_MESSAGE = (): string =>
  t('invoices.invoiceModel.cardStillConfirmingMessage');
export const CARD_START_FAILED_MESSAGE = (): string =>
  t('invoices.invoiceModel.cardStartFailedMessage');
export const CARD_UNAVAILABLE_MESSAGE = (): string =>
  t('invoices.invoiceModel.cardUnavailableMessage');
export const BANK_UNAVAILABLE_MESSAGE = (): string =>
  t('invoices.invoiceModel.bankUnavailableMessage');
export const BANK_NOTICE_FAILED_MESSAGE = (): string =>
  t('invoices.invoiceModel.bankNoticeFailedMessage');
export const REVIEW_FAILED_MESSAGE = (): string => t('invoices.invoiceModel.reviewFailedMessage');
export const NAME_REQUIRED_MESSAGE = (): string => t('invoices.invoiceModel.nameRequiredMessage');
export const NAME_TOO_LONG_MESSAGE = (): string => t('invoices.invoiceModel.nameTooLongMessage');
export const NAME_MAX_LENGTH = 120;
export const REVIEW_COMMENT_MAX_LENGTH = 500;

const DECLINED_CATEGORIES: readonly string[] = [
  'card_declined',
  'insufficient_funds',
  'incorrect_cvc',
  'expired_card',
  'authentication_failed',
];

/** BR-29 message for a failed attempt, keyed by `failureCategory`. */
export function failureMessage(category: string | null): string {
  if (category !== null && DECLINED_CATEGORIES.includes(category)) {
    return CARD_DECLINED_MESSAGE();
  }
  if (category === 'expired' || category === 'canceled') {
    return CARD_EXPIRED_MESSAGE();
  }
  return CARD_OTHER_FAILURE_MESSAGE();
}
