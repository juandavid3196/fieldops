import {
  EMAIL_INVALID_MESSAGE,
  EMAIL_REQUIRED_MESSAGE,
  FALLBACK_FIELD_MESSAGE,
  PASSWORD_REQUIRED_MESSAGE,
  PASSWORD_TOO_LONG_MESSAGE,
} from './sign-in.messages';

export const EMAIL_MAX_LENGTH = 254;
export const PASSWORD_MAX_LENGTH = 128;

export type SignInField = 'email' | 'password';

/** Field-level messages; a field is absent when valid. */
export type SignInFieldErrors = Partial<Record<SignInField, string>>;

const ALLOWED_MESSAGES: Readonly<Record<SignInField, readonly string[]>> = {
  email: [EMAIL_REQUIRED_MESSAGE, EMAIL_INVALID_MESSAGE],
  password: [PASSWORD_REQUIRED_MESSAGE, PASSWORD_TOO_LONG_MESSAGE],
};

/** Trims and lowercases the email before any check (BR-01). */
export function normalizeEmail(value: string): string {
  return value.trim().toLowerCase();
}

/**
 * First failing BR-01/BR-03 rule for the email, or `null` when valid.
 * Mirrors the backend rule: at most 254 characters, no whitespace, exactly
 * one `@` with a non-empty local part and a domain containing a dot.
 */
export function validateEmail(value: string): string | null {
  const email = normalizeEmail(value);
  if (email.length === 0) {
    return EMAIL_REQUIRED_MESSAGE;
  }
  return isValidEmail(email) ? null : EMAIL_INVALID_MESSAGE;
}

/** First failing BR-02/BR-03 rule for the password (never trimmed), or `null`. */
export function validatePassword(value: string): string | null {
  if (value.length === 0) {
    return PASSWORD_REQUIRED_MESSAGE;
  }
  return value.length > PASSWORD_MAX_LENGTH ? PASSWORD_TOO_LONG_MESSAGE : null;
}

/**
 * Maps server field errors (`400` ValidationProblemDetails) to the page's
 * field messages: only `email` and `password`, only the first message, and
 * any message outside BR-03 is replaced with a fixed fallback.
 */
export function mapServerFieldErrors(
  fieldErrors: Readonly<Record<string, readonly string[]>>,
): SignInFieldErrors {
  const result: SignInFieldErrors = {};
  for (const field of ['email', 'password'] as const) {
    const message = fieldErrors[field]?.[0];
    if (message !== undefined) {
      result[field] = ALLOWED_MESSAGES[field].includes(message) ? message : FALLBACK_FIELD_MESSAGE;
    }
  }
  return result;
}

function isValidEmail(email: string): boolean {
  if (email.length > EMAIL_MAX_LENGTH || /\s/.test(email)) {
    return false;
  }

  const at = email.indexOf('@');
  if (at <= 0 || email.indexOf('@', at + 1) >= 0) {
    return false;
  }

  return email.slice(at + 1).includes('.');
}
