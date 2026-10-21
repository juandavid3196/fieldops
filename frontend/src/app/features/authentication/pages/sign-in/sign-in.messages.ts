import { t } from '../../../../core/i18n/translate';
/** Field messages (BR-03). The only field messages the page displays. */
export const EMAIL_REQUIRED_MESSAGE = (): string =>
  t('authentication.signInMessages.emailRequiredMessage');
export const EMAIL_INVALID_MESSAGE = (): string =>
  t('authentication.signInMessages.emailInvalidMessage');
export const PASSWORD_REQUIRED_MESSAGE = (): string =>
  t('authentication.signInMessages.passwordRequiredMessage');
export const PASSWORD_TOO_LONG_MESSAGE = (): string =>
  t('authentication.signInMessages.passwordTooLongMessage');
/** Replaces any server field message outside BR-03. */
export const FALLBACK_FIELD_MESSAGE = (): string =>
  t('authentication.signInMessages.fallbackFieldMessage');

/** Page messages (BR-15). */
export const REGISTERED_MESSAGE = (): string =>
  t('authentication.signInMessages.registeredMessage');
export const INVALID_CREDENTIALS_MESSAGE = (): string =>
  t('authentication.signInMessages.invalidCredentialsMessage');
export const GENERIC_SIGN_IN_ERROR_MESSAGE = (): string =>
  t('authentication.signInMessages.genericSignInErrorMessage');

/** `429` message: `minutes` is already rounded up and at least 1. */
export function rateLimitedMessage(minutes: number): string {
  return minutes === 1
    ? t('authentication.signInMessages.tooManySignIn')
    : t('authentication.signInMessages.tooManySignIn2', { minutes: minutes });
}
