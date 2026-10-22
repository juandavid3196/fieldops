import { localized, t } from '../../core/i18n/translate';
export const LOAD_ERROR_MESSAGE = (): string =>
  t('serviceRequest.serviceRequestMessages.loadErrorMessage');
export const UNAVAILABLE_MESSAGE = (): string =>
  t('serviceRequest.serviceRequestMessages.unavailableMessage');
export const SUBMIT_ERROR_MESSAGE = (): string =>
  t('serviceRequest.serviceRequestMessages.submitErrorMessage');
export const RATE_LIMITED_MESSAGE = (): string =>
  t('serviceRequest.serviceRequestMessages.rateLimitedMessage');
export const ATTACHMENT_SIZE_MESSAGE = (): string =>
  t('serviceRequest.serviceRequestMessages.attachmentSizeMessage');
export const SERVER_FIELD_MESSAGE = (): string =>
  t('serviceRequest.serviceRequestMessages.serverFieldMessage');
export const SERVER_ATTACHMENTS_MESSAGE = (): string =>
  t('serviceRequest.serviceRequestMessages.serverAttachmentsMessage');

export const REQUIRED_MESSAGE = (): string =>
  t('serviceRequest.serviceRequestMessages.requiredMessage');
export const EMAIL_INVALID_MESSAGE = (): string =>
  t('serviceRequest.serviceRequestMessages.emailInvalidMessage');
export const PHONE_INVALID_MESSAGE = (): string =>
  t('serviceRequest.serviceRequestMessages.phoneInvalidMessage');
export const CONTACT_METHOD_MESSAGE = (): string =>
  t('serviceRequest.serviceRequestMessages.contactMethodMessage');
export const STATE_INVALID_MESSAGE = (): string =>
  t('serviceRequest.serviceRequestMessages.stateInvalidMessage');
export const POSTAL_CODE_INVALID_MESSAGE = (): string =>
  t('serviceRequest.serviceRequestMessages.postalCodeInvalidMessage');
export const CATEGORY_REQUIRED_MESSAGE = (): string =>
  t('serviceRequest.serviceRequestMessages.categoryRequiredMessage');
export const SERVICE_REQUIRED_MESSAGE = (): string =>
  t('serviceRequest.serviceRequestMessages.serviceRequiredMessage');
export const DESCRIPTION_REQUIRED_MESSAGE = (): string =>
  t('serviceRequest.serviceRequestMessages.descriptionRequiredMessage');
export const DATE_REQUIRED_MESSAGE = (): string =>
  t('serviceRequest.serviceRequestMessages.dateRequiredMessage');
export const DATE_RANGE_MESSAGE = (): string =>
  t('serviceRequest.serviceRequestMessages.dateRangeMessage');
export const CONSENT_REQUIRED_MESSAGE = (): string =>
  t('serviceRequest.serviceRequestMessages.consentRequiredMessage');

export function maxLengthMessage(max: number): string {
  return t('serviceRequest.serviceRequestMessages.useCharactersOrFewer', { max: max });
}

export const FILE_COUNT_MESSAGE = (name: string): string =>
  t('serviceRequest.serviceRequestMessages.wasNotAddedYou', { name: name });
export const FILE_TYPE_MESSAGE = (name: string): string =>
  t('serviceRequest.serviceRequestMessages.wasNotAddedOnly', { name: name });
export const FILE_EMPTY_MESSAGE = (name: string): string =>
  t('serviceRequest.serviceRequestMessages.wasNotAddedThe', { name: name });
export const FILE_SIZE_MESSAGE = (name: string): string =>
  t('serviceRequest.serviceRequestMessages.wasNotAddedFiles', { name: name });
export const FILE_TOTAL_MESSAGE = (name: string): string =>
  t('serviceRequest.serviceRequestMessages.wasNotAddedFiles2', { name: name });

export const URGENCY_LABELS = localized(
  () =>
    ({
      standard: t('serviceRequest.serviceRequestMessages.standard'),
      urgent: t('serviceRequest.serviceRequestMessages.urgent'),
      emergency: t('serviceRequest.serviceRequestMessages.emergency'),
    }) as const,
);

export const TIME_WINDOW_LABELS = localized(
  () =>
    ({
      morning: t('serviceRequest.serviceRequestMessages.morning8Am12'),
      afternoon: t('serviceRequest.serviceRequestMessages.afternoon12Pm5'),
      evening: t('serviceRequest.serviceRequestMessages.evening5Pm8'),
      any: t('serviceRequest.serviceRequestMessages.anyTime8Am'),
    }) as const,
);

export const NOT_SURE_LABEL = (): string => t('serviceRequest.serviceRequestMessages.notSureLabel');

/** Portal wizard (BR-28): a known property must be chosen. */
export const PORTAL_PROPERTY_REQUIRED_MESSAGE = (): string =>
  t('serviceRequest.serviceRequestMessages.portalPropertyRequiredMessage');
