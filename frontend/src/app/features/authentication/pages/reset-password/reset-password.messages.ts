import { t } from '../../../../core/i18n/translate';
/** Page copy (BR-21, BR-22). Backend text is never displayed. */
export const UNAVAILABLE_TITLE = (): string =>
  t('authentication.resetPasswordMessages.unavailableTitle');
export const UNAVAILABLE_BODY = (): string =>
  t('authentication.resetPasswordMessages.unavailableBody');
export const LOAD_ERROR_MESSAGE = (): string =>
  t('authentication.resetPasswordMessages.loadErrorMessage');
export const RESET_ERROR_MESSAGE = (): string =>
  t('authentication.resetPasswordMessages.resetErrorMessage');
