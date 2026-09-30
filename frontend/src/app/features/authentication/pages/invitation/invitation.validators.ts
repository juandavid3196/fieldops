import {
  FALLBACK_FIELD_MESSAGE,
  PASSWORD_EQUALS_EMAIL_MESSAGE,
  PASSWORD_LENGTH_MESSAGE,
  REQUIRED_MESSAGE,
  tooLongMessage,
} from '../../../organizations/pages/register-company/register-company.messages';
import {
  validateConfirmPassword,
  validatePassword,
} from '../../../organizations/pages/register-company/register-company.validators';
import { PASSWORD_REQUIRED_MESSAGE, PASSWORD_TOO_LONG_MESSAGE } from '../sign-in/sign-in.messages';
import { validatePassword as validateCurrentPassword } from '../sign-in/sign-in.validators';
import { FIRST_NAME_REQUIRED_MESSAGE, LAST_NAME_REQUIRED_MESSAGE } from './invitation.messages';

export type InvitationField = 'firstName' | 'lastName' | 'password' | 'confirmPassword';
export type InvitationFieldErrors = Partial<Record<InvitationField, string>>;

const NAME_MAX_LENGTH = 100;

/** BR-06 messages a server `400` may show per field; anything else becomes the fallback. */
const ALLOWED_MESSAGES: Readonly<Record<'firstName' | 'lastName' | 'password', readonly string[]>> =
  {
    firstName: [FIRST_NAME_REQUIRED_MESSAGE, tooLongMessage(NAME_MAX_LENGTH)],
    lastName: [LAST_NAME_REQUIRED_MESSAGE, tooLongMessage(NAME_MAX_LENGTH)],
    password: [
      REQUIRED_MESSAGE,
      PASSWORD_LENGTH_MESSAGE,
      PASSWORD_EQUALS_EMAIL_MESSAGE,
      PASSWORD_REQUIRED_MESSAGE,
      PASSWORD_TOO_LONG_MESSAGE,
    ],
  };

export function validateName(value: string, requiredMessage: string): string | null {
  const trimmed = value.trim();
  if (trimmed.length === 0) {
    return requiredMessage;
  }
  return trimmed.length > NAME_MAX_LENGTH ? tooLongMessage(NAME_MAX_LENGTH) : null;
}

/** New-account password rules (BR-06), reused from the onboarding validators. */
export { validateConfirmPassword, validatePassword as validateNewPassword };

/** Existing-account password rules (sign-in BR-02/BR-03). */
export { validateCurrentPassword as validateExistingPassword };

export interface PasswordRequirements {
  readonly lengthMet: boolean;
  readonly differsFromEmailMet: boolean;
}

export function passwordRequirements(password: string, email: string): PasswordRequirements {
  return {
    lengthMet: password.length >= 12 && password.length <= 128,
    differsFromEmailMet: password.toLowerCase() !== email.trim().toLowerCase(),
  };
}

/**
 * Maps server `400` field errors to the page's fields: only `firstName`,
 * `lastName` and `password`, only the first message, and any message outside
 * BR-06 is replaced with the fallback. Empty when no known field key is present.
 */
export function mapServerFieldErrors(
  fieldErrors: Readonly<Record<string, readonly string[]>>,
): InvitationFieldErrors {
  const result: InvitationFieldErrors = {};
  for (const field of ['firstName', 'lastName', 'password'] as const) {
    const message = fieldErrors[field]?.[0];
    if (message !== undefined) {
      result[field] = ALLOWED_MESSAGES[field].includes(message) ? message : FALLBACK_FIELD_MESSAGE;
    }
  }
  return result;
}

/** `true` when the only key of a `400` is `token` (BR-03/BR-17). */
export function isTokenOnly(fieldErrors: Readonly<Record<string, readonly string[]>>): boolean {
  const keys = Object.keys(fieldErrors);
  return keys.length === 1 && keys[0] === 'token';
}
