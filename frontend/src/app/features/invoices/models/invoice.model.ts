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

export interface PublicInvoice extends InvoicePreview {
  readonly status: 'sent';
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

export const PUBLIC_UNAVAILABLE_MESSAGE =
  "This invoice link isn't available. It may have expired or been replaced. Please contact the company that sent it.";
export const PUBLIC_RATE_LIMITED_MESSAGE =
  'Too many attempts. Please wait a few minutes and try again.';
export const PUBLIC_PDF_FAILED_MESSAGE = "We couldn't download the PDF. Please try again.";
export const PUBLIC_PAYMENT_NOTE = 'Online payment will be available soon.';
