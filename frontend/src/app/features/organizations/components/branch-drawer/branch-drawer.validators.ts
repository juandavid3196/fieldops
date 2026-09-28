import {
  BRANCH_CODE_MESSAGE,
  COUNTRY_MESSAGE,
  DUPLICATE_BRANCH_CODE_MESSAGE,
  EMAIL_INVALID_MESSAGE,
  END_NOT_AFTER_START_MESSAGE,
  FALLBACK_FIELD_MESSAGE,
  PHONE_INVALID_MESSAGE,
  REQUIRED_MESSAGE,
  TIMEZONE_MESSAGE,
  tooLongMessage,
} from '../../pages/register-company/register-company.messages';
import {
  EMAIL_MAX_LENGTH,
  PHONE_MAX_LENGTH,
  businessHoursEndKey,
  businessHoursStartKey,
  validateBranchCode,
  validateCountryCode,
  validateOptionalEmail,
  validateOptionalPhone,
  validateOptionalText,
  validateRequiredText,
  validateTimezone,
} from '../../pages/register-company/register-company.validators';
import {
  BranchDrawerFormValue,
  BranchFieldErrors,
  BranchFieldKey,
  BranchSimpleFieldKey,
} from '../../models/company-settings.model';
import { WEEKDAYS, WEEKDAY_LABELS, Weekday } from '../../models/organization-registration.model';

/** Every simple branch field, in Name / Code / contact / address section order. */
export const BRANCH_SIMPLE_FIELD_KEYS: readonly BranchSimpleFieldKey[] = [
  'branch.name',
  'branch.code',
  'branch.phone',
  'branch.email',
  'branch.timezone',
  'branch.addressLine1',
  'branch.addressLine2',
  'branch.city',
  'branch.stateRegion',
  'branch.postalCode',
  'branch.countryCode',
];

/** Server request key for each simple field (the API body has no `branch.` prefix). */
const REQUEST_KEYS: Readonly<Record<BranchSimpleFieldKey, keyof BranchDrawerFormValue>> = {
  'branch.name': 'name',
  'branch.code': 'code',
  'branch.phone': 'phone',
  'branch.email': 'email',
  'branch.timezone': 'timezone',
  'branch.addressLine1': 'addressLine1',
  'branch.addressLine2': 'addressLine2',
  'branch.city': 'city',
  'branch.stateRegion': 'stateRegion',
  'branch.postalCode': 'postalCode',
  'branch.countryCode': 'countryCode',
};

/** Visible labels, shared by field labels and the error summary link text. */
export const BRANCH_FIELD_LABELS: Readonly<Record<BranchFieldKey, string>> = buildFieldLabels();

const ALLOWED_MESSAGES: Readonly<Record<BranchSimpleFieldKey, readonly string[]>> = {
  'branch.name': [REQUIRED_MESSAGE, tooLongMessage(140)],
  'branch.code': [REQUIRED_MESSAGE, BRANCH_CODE_MESSAGE, DUPLICATE_BRANCH_CODE_MESSAGE],
  'branch.phone': [tooLongMessage(PHONE_MAX_LENGTH), PHONE_INVALID_MESSAGE],
  'branch.email': [tooLongMessage(EMAIL_MAX_LENGTH), EMAIL_INVALID_MESSAGE],
  'branch.timezone': [TIMEZONE_MESSAGE],
  'branch.addressLine1': [REQUIRED_MESSAGE, tooLongMessage(180)],
  'branch.addressLine2': [tooLongMessage(180)],
  'branch.city': [REQUIRED_MESSAGE, tooLongMessage(100)],
  'branch.stateRegion': [tooLongMessage(100)],
  'branch.postalCode': [REQUIRED_MESSAGE, tooLongMessage(30)],
  'branch.countryCode': [COUNTRY_MESSAGE],
};

/** First failing BR-03 rule for one simple field, or `null` when valid. */
export function validateBranchField(
  field: BranchSimpleFieldKey,
  value: BranchDrawerFormValue,
): string | null {
  switch (field) {
    case 'branch.name':
      return validateRequiredText(value.name, 140);
    case 'branch.code':
      return validateBranchCode(value.code);
    case 'branch.phone':
      return validateOptionalPhone(value.phone);
    case 'branch.email':
      return validateOptionalEmail(value.email);
    case 'branch.timezone':
      return validateTimezone(value.timezone);
    case 'branch.addressLine1':
      return validateRequiredText(value.addressLine1, 180);
    case 'branch.addressLine2':
      return validateOptionalText(value.addressLine2, 180);
    case 'branch.city':
      return validateRequiredText(value.city, 100);
    case 'branch.stateRegion':
      return validateOptionalText(value.stateRegion, 100);
    case 'branch.postalCode':
      return validateRequiredText(value.postalCode, 30);
    case 'branch.countryCode':
      return validateCountryCode(value.countryCode);
  }
}

/**
 * Maps the flat `POST`/`PUT /branches` field errors (`400`/`409`) to the
 * `branch.*` UI keys the reused business-hours editor and this drawer expect
 * (BR-13): only the first message per known key, and any message outside its
 * allow-list becomes the fallback.
 */
export function mapBranchServerFieldErrors(
  fieldErrors: Readonly<Record<string, readonly string[]>>,
): BranchFieldErrors {
  const result: BranchFieldErrors = {};

  for (const field of BRANCH_SIMPLE_FIELD_KEYS) {
    const message = fieldErrors[REQUEST_KEYS[field]]?.[0];
    if (message !== undefined) {
      result[field] = ALLOWED_MESSAGES[field].includes(message) ? message : FALLBACK_FIELD_MESSAGE;
    }
  }

  for (const day of WEEKDAYS) {
    for (const part of ['start', 'end'] as const) {
      const serverKey = `businessHours.${day}.${part}`;
      const message = fieldErrors[serverKey]?.[0];
      if (message !== undefined) {
        const uiKey = part === 'start' ? businessHoursStartKey(day) : businessHoursEndKey(day);
        result[uiKey] =
          message === REQUIRED_MESSAGE || message === END_NOT_AFTER_START_MESSAGE
            ? message
            : FALLBACK_FIELD_MESSAGE;
      }
    }
  }

  if (fieldErrors['businessHours']?.[0] !== undefined) {
    result['branch.businessHours'] = FALLBACK_FIELD_MESSAGE;
  }

  return result;
}

function buildFieldLabels(): Record<BranchFieldKey, string> {
  const labels: Record<string, string> = {
    'branch.name': 'Branch name',
    'branch.code': 'Branch code',
    'branch.phone': 'Contact phone',
    'branch.email': 'Contact email',
    'branch.timezone': 'Time zone',
    'branch.addressLine1': 'Address line 1',
    'branch.addressLine2': 'Address line 2',
    'branch.city': 'City',
    'branch.stateRegion': 'State/Region',
    'branch.postalCode': 'Postal code',
    'branch.countryCode': 'Country',
    'branch.businessHours': 'Business hours',
  };

  for (const day of WEEKDAYS as readonly Weekday[]) {
    labels[businessHoursStartKey(day)] = `${WEEKDAY_LABELS[day]} start time`;
    labels[businessHoursEndKey(day)] = `${WEEKDAY_LABELS[day]} end time`;
  }

  return labels as Record<BranchFieldKey, string>;
}
