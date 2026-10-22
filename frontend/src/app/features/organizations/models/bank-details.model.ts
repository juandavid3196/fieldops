import { localized, t } from '../../../core/i18n/translate';
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

export const BANK_NAME_MESSAGE = (): string => t('organizations.bankDetailsModel.bankNameMessage');
export const BANK_ACCOUNT_MESSAGE = (): string =>
  t('organizations.bankDetailsModel.bankAccountMessage');
export const BANK_ROUTING_MESSAGE = (): string =>
  t('organizations.bankDetailsModel.bankRoutingMessage');
export const BANK_SAVED_MESSAGE = (): string =>
  t('organizations.bankDetailsModel.bankSavedMessage');
export const BANK_CHANGED_MESSAGE = (): string =>
  t('organizations.bankDetailsModel.bankChangedMessage');
export const BANK_UNAVAILABLE_MESSAGE = (): string =>
  t('organizations.bankDetailsModel.bankUnavailableMessage');
export const BANK_SAVE_FAILED_MESSAGE = (): string =>
  t('organizations.bankDetailsModel.bankSaveFailedMessage');
export const BANK_LOAD_ERROR_MESSAGE = (): string =>
  t('organizations.bankDetailsModel.bankLoadErrorMessage');
export const BANK_HELPER_MESSAGE = (): string =>
  t('organizations.bankDetailsModel.bankHelperMessage');
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
    errors.bankName = BANK_NAME_MESSAGE();
  }
  const account = values.accountNumber.trim();
  if ((account === '' && accountRequired) || (account !== '' && !ACCOUNT_PATTERN.test(account))) {
    errors.accountNumber = BANK_ACCOUNT_MESSAGE();
  }
  if (!ROUTING_PATTERN.test(values.routingNumber.trim())) {
    errors.routingNumber = BANK_ROUTING_MESSAGE();
  }
  return errors;
}

export const BANK_FIELD_MESSAGES: Readonly<Record<BankField, string>> = localized(() => ({
  bankName: BANK_NAME_MESSAGE(),
  accountNumber: BANK_ACCOUNT_MESSAGE(),
  routingNumber: BANK_ROUTING_MESSAGE(),
}));
