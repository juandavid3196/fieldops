import { t } from '../../../../core/i18n/translate';
/** Field messages (BR-06) that are specific to the invitation page. */
export const FIRST_NAME_REQUIRED_MESSAGE = (): string =>
  t('authentication.invitationMessages.firstNameRequiredMessage');
export const LAST_NAME_REQUIRED_MESSAGE = (): string =>
  t('authentication.invitationMessages.lastNameRequiredMessage');

/** Page copy (BR-17). Backend text is never displayed. */
export const UNAVAILABLE_TITLE = (): string =>
  t('authentication.invitationMessages.unavailableTitle');
export const UNAVAILABLE_BODY = (): string =>
  t('authentication.invitationMessages.unavailableBody');
export const UNAVAILABLE_NOTE = (): string =>
  t('authentication.invitationMessages.unavailableNote');
export const LOAD_ERROR_MESSAGE = (): string =>
  t('authentication.invitationMessages.loadErrorMessage');
export const ACCOUNT_EXISTS_MESSAGE = (): string =>
  t('authentication.invitationMessages.accountExistsMessage');
export const INVALID_CREDENTIALS_MESSAGE = (): string =>
  t('authentication.invitationMessages.invalidCredentialsMessage');
export const GENERIC_ACCEPT_ERROR_MESSAGE = (): string =>
  t('authentication.invitationMessages.genericAcceptErrorMessage');

export function cannotAcceptMessage(organizationName: string): string {
  return t('authentication.invitationMessages.thisInvitationCantBe', {
    organizationName: organizationName,
  });
}

/** `429` message: `minutes` is already rounded up and at least 1. */
export function rateLimitedMessage(minutes: number): string {
  return minutes === 1
    ? t('authentication.invitationMessages.tooManyAttemptsTry')
    : t('authentication.invitationMessages.tooManyAttemptsTry2', { minutes: minutes });
}

export function expiryNote(days: number): string {
  return t('authentication.invitationMessages.thisInvitationCanOnly', {
    value:
      days === 1
        ? t('authentication.invitationMessages.n1Day')
        : t('authentication.invitationMessages.days', { days: days }),
  });
}
