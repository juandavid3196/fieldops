/** Field messages (BR-03). The only field messages the page displays. */
export const EMAIL_REQUIRED_MESSAGE = 'Enter your email address.';
export const EMAIL_INVALID_MESSAGE = 'Enter a valid email address, for example name@company.com.';
export const PASSWORD_REQUIRED_MESSAGE = 'Enter your password.';
export const PASSWORD_TOO_LONG_MESSAGE = 'Use 128 characters or fewer.';
/** Replaces any server field message outside BR-03. */
export const FALLBACK_FIELD_MESSAGE = 'Enter a valid value.';

/** Page messages (BR-15). */
export const REGISTERED_MESSAGE = 'Your organization was created. Sign in to continue.';
export const INVALID_CREDENTIALS_MESSAGE =
  'The email or password is incorrect. Check your details and try again.';
export const GENERIC_SIGN_IN_ERROR_MESSAGE =
  "We couldn't sign you in right now. Try again in a moment.";

/** `429` message: `minutes` is already rounded up and at least 1. */
export function rateLimitedMessage(minutes: number): string {
  return minutes === 1
    ? 'Too many sign-in attempts. Try again in 1 minute.'
    : `Too many sign-in attempts. Try again in ${minutes} minutes.`;
}
