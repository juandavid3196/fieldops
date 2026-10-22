import { t } from '../../../../core/i18n/translate';
/** BR-24 message catalog: the only field messages this page ever shows. */
export const REQUIRED_MESSAGE = (): string =>
  t('organizations.registerCompanyMessages.requiredMessage');
export const EMAIL_INVALID_MESSAGE = (): string =>
  t('organizations.registerCompanyMessages.emailInvalidMessage');
export const PHONE_INVALID_MESSAGE = (): string =>
  t('organizations.registerCompanyMessages.phoneInvalidMessage');
export const TIMEZONE_MESSAGE = (): string =>
  t('organizations.registerCompanyMessages.timezoneMessage');
export const CURRENCY_MESSAGE = (): string =>
  t('organizations.registerCompanyMessages.currencyMessage');
export const COUNTRY_MESSAGE = (): string =>
  t('organizations.registerCompanyMessages.countryMessage');
export const TAX_RATE_MESSAGE = (): string =>
  t('organizations.registerCompanyMessages.taxRateMessage');
export const PREFIX_MESSAGE = (): string =>
  t('organizations.registerCompanyMessages.prefixMessage');
export const BRANCH_CODE_MESSAGE = (): string =>
  t('organizations.registerCompanyMessages.branchCodeMessage');
export const NEXT_INVOICE_NUMBER_MESSAGE = (): string =>
  t('organizations.registerCompanyMessages.nextInvoiceNumberMessage');
export const END_NOT_AFTER_START_MESSAGE = (): string =>
  t('organizations.registerCompanyMessages.endNotAfterStartMessage');
export const PASSWORD_LENGTH_MESSAGE = (): string =>
  t('organizations.registerCompanyMessages.passwordLengthMessage');
export const PASSWORD_EQUALS_EMAIL_MESSAGE = (): string =>
  t('organizations.registerCompanyMessages.passwordEqualsEmailMessage');
export const PASSWORDS_DONT_MATCH_MESSAGE = (): string =>
  t('organizations.registerCompanyMessages.passwordsDontMatchMessage');
export const DUPLICATE_EMAIL_MESSAGE = (): string =>
  t('organizations.registerCompanyMessages.duplicateEmailMessage');
/** BR-02 (company-settings-and-branches): `nextInvoiceNumber` at or below the floor. */
export const NEXT_INVOICE_NUMBER_FLOOR_MESSAGE = (): string =>
  t('organizations.registerCompanyMessages.nextInvoiceNumberFloorMessage');
/** BR-04 (company-settings-and-branches): duplicate branch `code` within the organization. */
export const DUPLICATE_BRANCH_CODE_MESSAGE = (): string =>
  t('organizations.registerCompanyMessages.duplicateBranchCodeMessage');
/** Replaces any server field message outside the BR-24 catalog. */
export const FALLBACK_FIELD_MESSAGE = (): string =>
  t('organizations.registerCompanyMessages.fallbackFieldMessage');

/** "Use {n} characters or fewer." for the field's maximum length. */
export function tooLongMessage(maxLength: number): string {
  return t('organizations.registerCompanyMessages.useCharactersOrFewer', { maxLength: maxLength });
}
