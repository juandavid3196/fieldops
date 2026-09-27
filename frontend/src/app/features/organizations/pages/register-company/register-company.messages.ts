/** BR-24 message catalog: the only field messages this page ever shows. */
export const REQUIRED_MESSAGE = 'This field is required.';
export const EMAIL_INVALID_MESSAGE = 'Enter a valid email address, for example name@company.com.';
export const PHONE_INVALID_MESSAGE = 'Enter a valid phone number.';
export const TIMEZONE_MESSAGE = 'Select a time zone.';
export const CURRENCY_MESSAGE = 'Select a currency.';
export const COUNTRY_MESSAGE = 'Select a country.';
export const TAX_RATE_MESSAGE = 'Enter a rate between 0 and 100 with up to 4 decimals.';
export const PREFIX_MESSAGE = 'Use 1–20 characters: letters A–Z, numbers and hyphens.';
export const BRANCH_CODE_MESSAGE = 'Use 1–8 characters: letters A–Z, numbers and hyphens.';
export const NEXT_INVOICE_NUMBER_MESSAGE = 'Enter a whole number from 1 to 999,999,999,999.';
export const END_NOT_AFTER_START_MESSAGE = 'End time must be after start time.';
export const PASSWORD_LENGTH_MESSAGE = 'Use 12 to 128 characters.';
export const PASSWORD_EQUALS_EMAIL_MESSAGE = 'Choose a password that is different from your email.';
export const PASSWORDS_DONT_MATCH_MESSAGE = "Passwords don't match.";
export const DUPLICATE_EMAIL_MESSAGE =
  'An account with this email already exists. Sign in instead.';
/** Replaces any server field message outside the BR-24 catalog. */
export const FALLBACK_FIELD_MESSAGE = 'Enter a valid value.';

/** "Use {n} characters or fewer." for the field's maximum length. */
export function tooLongMessage(maxLength: number): string {
  return `Use ${maxLength} characters or fewer.`;
}
