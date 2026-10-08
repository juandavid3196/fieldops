import { TERMS_OPTIONS } from '../../billing-review/utils/billing-review-format';
import { initialsOf } from '../../requests/utils/requests-format';
import {
  DeliveryBody,
  DeliveryErrors,
  DeliveryField,
  DeliveryValues,
  EMAIL_MAX_LENGTH,
  InvoiceDetail,
  MESSAGE_MAX_LENGTH,
  MESSAGE_REQUIRED_MESSAGE,
  MESSAGE_TOO_LONG_MESSAGE,
  RECIPIENT_INVALID_MESSAGE,
  RECIPIENT_REQUIRED_MESSAGE,
  TERMS_MESSAGE,
} from '../models/invoice.model';

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/** Field order of the panel: the first invalid one receives focus (BR-26). */
export const DELIVERY_FIELD_ORDER: readonly DeliveryField[] = [
  'recipientEmail',
  'paymentTerms',
  'message',
];

export function initialValues(invoice: InvoiceDetail): DeliveryValues {
  return {
    paymentTerms: invoice.paymentTerms,
    recipientEmail: invoice.delivery.recipientEmail ?? '',
    message: invoice.delivery.message ?? '',
  };
}

export function sameValues(a: DeliveryValues, b: DeliveryValues): boolean {
  return (
    a.paymentTerms === b.paymentTerms &&
    a.recipientEmail.trim() === b.recipientEmail.trim() &&
    a.message.trim() === b.message.trim()
  );
}

/** Delivery field rules (spec "Delivery field rules"); the server decides again. */
export function validateDelivery(values: DeliveryValues, mode: 'save' | 'send'): DeliveryErrors {
  const errors: DeliveryErrors = {};
  const recipient = values.recipientEmail.trim();
  if (recipient === '') {
    if (mode === 'send') {
      errors.recipientEmail = RECIPIENT_REQUIRED_MESSAGE;
    }
  } else if (recipient.length > EMAIL_MAX_LENGTH || !EMAIL_PATTERN.test(recipient)) {
    errors.recipientEmail = RECIPIENT_INVALID_MESSAGE;
  }
  if (!TERMS_OPTIONS.some((option) => option.code === values.paymentTerms)) {
    errors.paymentTerms = TERMS_MESSAGE;
  }
  const length = values.message.trim().length;
  if (length === 0) {
    errors.message = MESSAGE_REQUIRED_MESSAGE;
  } else if (length > MESSAGE_MAX_LENGTH) {
    errors.message = MESSAGE_TOO_LONG_MESSAGE;
  }
  return errors;
}

/** Field messages for the keys of a `400` (backend text is never shown). */
export function serverErrors(
  keys: readonly string[],
  values: DeliveryValues,
  mode: 'save' | 'send',
): DeliveryErrors {
  const lower = keys.map((key) => key.toLowerCase());
  const errors = validateDelivery(values, mode);
  if (lower.includes('recipientemail') && errors.recipientEmail === undefined) {
    errors.recipientEmail = RECIPIENT_INVALID_MESSAGE;
  }
  if (lower.includes('paymentterms') && errors.paymentTerms === undefined) {
    errors.paymentTerms = TERMS_MESSAGE;
  }
  if (lower.includes('message') && errors.message === undefined) {
    errors.message = MESSAGE_REQUIRED_MESSAGE;
  }
  return errors;
}

export function firstInvalid(errors: DeliveryErrors): DeliveryField | null {
  return DELIVERY_FIELD_ORDER.find((field) => errors[field] !== undefined) ?? null;
}

/** Request body; `null` when the terms are not chosen (validation rejects that first). */
export function toBody(values: DeliveryValues, updatedAt: string): DeliveryBody | null {
  return values.paymentTerms === null
    ? null
    : {
        paymentTerms: values.paymentTerms,
        recipientEmail: values.recipientEmail.trim(),
        message: values.message.trim(),
        updatedAt,
      };
}

/** Quantity without trailing zeros, with its unit unless the unit is the default `unit`. */
export function quantityText(quantity: string, unit: string): string {
  const plain = /^\d+\.\d+$/.test(quantity) ? quantity.replace(/\.?0+$/, '') : quantity;
  const suffix = unit.trim() === '' || unit === 'unit' ? '' : ` ${unit}`;
  return `${plain}${suffix}`;
}

/** "<r>%" without trailing zeros, "—" when 0. */
export function taxText(rate: number): string {
  return rate === 0 ? '—' : `${Number(rate.toFixed(4))}%`;
}

export function statusLabel(status: string): string {
  switch (status) {
    case 'draft':
      return 'Draft';
    case 'sent':
      return 'Sent';
    default:
      return status.charAt(0).toUpperCase() + status.slice(1).replace(/_/g, ' ');
  }
}

export function organizationInitials(name: string): string {
  return initialsOf(name) || '?';
}

/** Non-empty `Content-Disposition` file name, else the fallback. */
export function pdfFileName(disposition: string | null, fallback: string): string {
  const name = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition ?? '')?.[1]?.trim();
  return name !== undefined && /^[\w.-]+$/.test(name) ? name : fallback;
}
