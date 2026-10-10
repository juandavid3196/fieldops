/** Contracts of specs/customer-invoice-payments/spec.md (Final), BR-25. */
export interface BankDetails {
  readonly configured: boolean;
  readonly bankName: string | null;
  /** "•••• <last4>"; the full number is never returned. */
  readonly accountNumberMasked: string | null;
  readonly routingNumber: string | null;
  readonly updatedAt: string | null;
}

export interface BankDetailsRequest {
  readonly bankName: string;
  /** Empty keeps the stored number. */
  readonly accountNumber: string;
  readonly routingNumber: string;
  readonly updatedAt: string | null;
}

export type BankField = 'bankName' | 'accountNumber' | 'routingNumber';
export type BankFieldErrors = Partial<Record<BankField, string>>;

export const BANK_NAME_MESSAGE = 'Enter the bank name.';
export const BANK_ACCOUNT_MESSAGE = 'Enter a valid account number.';
export const BANK_ROUTING_MESSAGE = 'Enter a 9-digit routing number.';
export const BANK_SAVED_MESSAGE = 'Bank details saved.';
export const BANK_CHANGED_MESSAGE = 'These details changed. Refresh to see the latest.';
export const BANK_UNAVAILABLE_MESSAGE = "Bank details can't be saved right now.";
export const BANK_SAVE_FAILED_MESSAGE = "We couldn't save bank details. Try again.";
export const BANK_LOAD_ERROR_MESSAGE = "We couldn't load bank details.";
export const BANK_HELPER_MESSAGE = 'Customers see these details on unpaid invoices.';
export const BANK_NAME_MAX_LENGTH = 120;

const ACCOUNT_PATTERN = /^\d{4,17}$/;
const ROUTING_PATTERN = /^\d{9}$/;

/** BR-25 field rules; the server decides again. */
export function validateBankDetails(
  values: { bankName: string; accountNumber: string; routingNumber: string },
  accountRequired: boolean,
): BankFieldErrors {
  const errors: BankFieldErrors = {};
  const name = values.bankName.trim();
  if (name === '' || name.length > BANK_NAME_MAX_LENGTH) {
    errors.bankName = BANK_NAME_MESSAGE;
  }
  const account = values.accountNumber.trim();
  if ((account === '' && accountRequired) || (account !== '' && !ACCOUNT_PATTERN.test(account))) {
    errors.accountNumber = BANK_ACCOUNT_MESSAGE;
  }
  if (!ROUTING_PATTERN.test(values.routingNumber.trim())) {
    errors.routingNumber = BANK_ROUTING_MESSAGE;
  }
  return errors;
}

export const BANK_FIELD_MESSAGES: Readonly<Record<BankField, string>> = {
  bankName: BANK_NAME_MESSAGE,
  accountNumber: BANK_ACCOUNT_MESSAGE,
  routingNumber: BANK_ROUTING_MESSAGE,
};
