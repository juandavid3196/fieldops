import { PaymentTerms } from '../../billing-review/models/billing-review.model';

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

export const FORBIDDEN_MESSAGE = "You don't have access to invoices.";
export const UNAVAILABLE_MESSAGE = "This invoice isn't available.";
export const LOAD_ERROR_MESSAGE = "We couldn't load this invoice. Try again.";
export const READ_ONLY_MESSAGE = 'Only owners and accounting can edit and send invoices.';
export const ACTION_FAILED_MESSAGE = "We couldn't complete this action. Try again.";
export const PDF_FAILED_MESSAGE = "We couldn't download the PDF. Try again.";
export const SMS_UNAVAILABLE_MESSAGE = "SMS isn't available yet.";
export const LATER_MESSAGE = 'Online payments and attachments will be available later.';
export const EMAIL_FAILED_MESSAGE =
  "Invoice sent, but we couldn't email it. Use Resend email to try again.";
export const TERMS_MESSAGE = 'Select payment terms.';
export const RECIPIENT_REQUIRED_MESSAGE = "Enter the customer's email address.";
export const RECIPIENT_INVALID_MESSAGE = 'Enter a valid email address.';
export const MESSAGE_REQUIRED_MESSAGE = 'Enter a message.';
export const MESSAGE_TOO_LONG_MESSAGE = 'Message must be 500 characters or fewer.';

/** Fixed copy for the `409` codes (backend titles are never displayed). */
export const CONFLICT_MESSAGES: Readonly<Record<string, string>> = {
  invoice_changed: 'This invoice changed. Refresh to see the latest.',
  invoice_not_draft: "This invoice has already been sent and can't be edited.",
  invoice_not_sent: 'Only sent invoices can be emailed again.',
};

/** BR-34 neutral state. */
export const PUBLIC_UNAVAILABLE_MESSAGE = 'This invoice is no longer available';
export const PUBLIC_UNAVAILABLE_DETAIL =
  'This link can no longer be used. Contact the company if you need a new invoice link.';
export const PUBLIC_PROTECTION_MESSAGE = 'For your protection, invoice details are not shown.';
export const PUBLIC_RATE_LIMITED_MESSAGE =
  'Too many attempts. Please wait a few minutes and try again.';
export const PUBLIC_DOWNLOAD_FAILED_MESSAGE = "We couldn't download the file. Please try again.";

/** BR-29 card flow copy. */
export const CARD_FAILURE_TITLE = "Payment wasn't completed";
export const CARD_DECLINED_MESSAGE =
  'Your card was declined. Check the details or try another payment method.';
export const CARD_EXPIRED_MESSAGE = 'The payment session expired. Try again.';
export const CARD_OTHER_FAILURE_MESSAGE =
  "We couldn't process your payment. Try again or use another payment method.";
export const CARD_STILL_CONFIRMING_MESSAGE =
  "We're still confirming your payment. You can safely close this page; we'll email your receipt.";
export const CARD_START_FAILED_MESSAGE = "We couldn't start the payment. Try again.";
export const CARD_UNAVAILABLE_MESSAGE = "Card payments aren't available right now.";
export const BANK_UNAVAILABLE_MESSAGE = "Bank transfer isn't available for this invoice.";
export const BANK_NOTICE_FAILED_MESSAGE = "We couldn't send your notice. Try again.";
export const REVIEW_FAILED_MESSAGE = "We couldn't send your review. Try again.";
export const NAME_REQUIRED_MESSAGE = 'Enter the cardholder name.';
export const NAME_TOO_LONG_MESSAGE = 'Use 120 characters or fewer.';
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
    return CARD_DECLINED_MESSAGE;
  }
  if (category === 'expired' || category === 'canceled') {
    return CARD_EXPIRED_MESSAGE;
  }
  return CARD_OTHER_FAILURE_MESSAGE;
}
