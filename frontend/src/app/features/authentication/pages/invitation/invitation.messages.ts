/** Field messages (BR-06) that are specific to the invitation page. */
export const FIRST_NAME_REQUIRED_MESSAGE = 'Enter a first name.';
export const LAST_NAME_REQUIRED_MESSAGE = 'Enter a last name.';

/** Page copy (BR-17). Backend text is never displayed. */
export const UNAVAILABLE_TITLE = 'This invitation is no longer available';
export const UNAVAILABLE_BODY =
  'This invitation can no longer be used. Ask an Owner at your organization to send you a new one.';
export const UNAVAILABLE_NOTE =
  'For your security, expired and replaced invitation links cannot be reactivated.';
export const LOAD_ERROR_MESSAGE =
  "We couldn't load this invitation. Check your connection and try again.";
export const ACCOUNT_EXISTS_MESSAGE =
  'An account already exists for this email. Sign in to accept.';
export const INVALID_CREDENTIALS_MESSAGE =
  'The email or password is incorrect. Check your details and try again.';
export const GENERIC_ACCEPT_ERROR_MESSAGE =
  "We couldn't accept the invitation right now. Try again in a moment.";

export function cannotAcceptMessage(organizationName: string): string {
  return `This invitation can't be accepted with this account. Contact an Owner at ${organizationName}.`;
}

/** `429` message: `minutes` is already rounded up and at least 1. */
export function rateLimitedMessage(minutes: number): string {
  return minutes === 1
    ? 'Too many attempts. Try again in 1 minute.'
    : `Too many attempts. Try again in ${minutes} minutes.`;
}

export function expiryNote(days: number): string {
  return `This invitation can only be used once and expires in ${days === 1 ? '1 day' : `${days} days`}.`;
}
