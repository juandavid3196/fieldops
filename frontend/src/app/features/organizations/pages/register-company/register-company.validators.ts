import { SUPPORTED_COUNTRY_CODES } from '../../data/supported-countries';
import { SUPPORTED_CURRENCY_CODES } from '../../data/supported-currencies';
import {
  FieldErrors,
  FieldKey,
  RegisterCompanyFormValue,
  SimpleFieldKey,
  Weekday,
} from '../../models/organization-registration.model';
import {
  BRANCH_CODE_MESSAGE,
  COUNTRY_MESSAGE,
  CURRENCY_MESSAGE,
  DUPLICATE_EMAIL_MESSAGE,
  EMAIL_INVALID_MESSAGE,
  END_NOT_AFTER_START_MESSAGE,
  FALLBACK_FIELD_MESSAGE,
  NEXT_INVOICE_NUMBER_MESSAGE,
  PASSWORDS_DONT_MATCH_MESSAGE,
  PASSWORD_EQUALS_EMAIL_MESSAGE,
  PASSWORD_LENGTH_MESSAGE,
  PHONE_INVALID_MESSAGE,
  PREFIX_MESSAGE,
  REQUIRED_MESSAGE,
  TAX_RATE_MESSAGE,
  TIMEZONE_MESSAGE,
  tooLongMessage,
} from './register-company.messages';

export const EMAIL_MAX_LENGTH = 254;
export const PHONE_MAX_LENGTH = 40;

const SUPPORTED_TIME_ZONES = new Set(Intl.supportedValuesOf('timeZone'));
const SUPPORTED_CURRENCIES = new Set(SUPPORTED_CURRENCY_CODES);
const SUPPORTED_COUNTRIES = new Set(SUPPORTED_COUNTRY_CODES);

/** Visible labels, shared by field labels and the error summary link text. */
export const FIELD_LABELS: Readonly<Record<FieldKey, string>> = {
  'organization.name': 'Display name',
  'organization.legalName': 'Legal business name',
  'organization.taxId': 'Tax ID',
  'organization.email': 'Business email',
  'organization.phone': 'Business phone',
  'organization.timezone': 'Company time zone',
  'organization.currency': 'Currency',
  'organization.defaultTaxRate': 'Default tax rate',
  'organization.quotePrefix': 'Quote prefix',
  'organization.workOrderPrefix': 'Work order prefix',
  'organization.invoicePrefix': 'Invoice prefix',
  'organization.nextInvoiceNumber': 'Next invoice number',
  'branch.name': 'Branch name',
  'branch.code': 'Branch code',
  'branch.phone': 'Branch phone',
  'branch.email': 'Branch email',
  'branch.timezone': 'Branch time zone',
  'branch.addressLine1': 'Address line 1',
  'branch.city': 'City',
  'branch.stateRegion': 'State/Region',
  'branch.postalCode': 'Postal code',
  'branch.countryCode': 'Default country',
  'branch.businessHours': 'Business hours',
  'branch.businessHours.monday.start': 'Monday start time',
  'branch.businessHours.monday.end': 'Monday end time',
  'branch.businessHours.tuesday.start': 'Tuesday start time',
  'branch.businessHours.tuesday.end': 'Tuesday end time',
  'branch.businessHours.wednesday.start': 'Wednesday start time',
  'branch.businessHours.wednesday.end': 'Wednesday end time',
  'branch.businessHours.thursday.start': 'Thursday start time',
  'branch.businessHours.thursday.end': 'Thursday end time',
  'branch.businessHours.friday.start': 'Friday start time',
  'branch.businessHours.friday.end': 'Friday end time',
  'branch.businessHours.saturday.start': 'Saturday start time',
  'branch.businessHours.saturday.end': 'Saturday end time',
  'branch.businessHours.sunday.start': 'Sunday start time',
  'branch.businessHours.sunday.end': 'Sunday end time',
  'owner.firstName': 'First name',
  'owner.lastName': 'Last name',
  'owner.email': 'Email',
  'owner.phone': 'Phone',
  'owner.password': 'Password',
  confirmPassword: 'Confirm password',
};

/** The BR-24 catalog messages a field is allowed to show; anything else becomes the fallback. */
const ALLOWED_MESSAGES: Readonly<Record<SimpleFieldKey, readonly string[]>> = {
  'organization.name': [REQUIRED_MESSAGE, tooLongMessage(160)],
  'organization.legalName': [REQUIRED_MESSAGE, tooLongMessage(200)],
  'organization.taxId': [tooLongMessage(60)],
  'organization.email': [REQUIRED_MESSAGE, tooLongMessage(EMAIL_MAX_LENGTH), EMAIL_INVALID_MESSAGE],
  'organization.phone': [REQUIRED_MESSAGE, tooLongMessage(PHONE_MAX_LENGTH), PHONE_INVALID_MESSAGE],
  'organization.timezone': [TIMEZONE_MESSAGE],
  'organization.currency': [CURRENCY_MESSAGE],
  'organization.defaultTaxRate': [REQUIRED_MESSAGE, TAX_RATE_MESSAGE],
  'organization.quotePrefix': [REQUIRED_MESSAGE, PREFIX_MESSAGE],
  'organization.workOrderPrefix': [REQUIRED_MESSAGE, PREFIX_MESSAGE],
  'organization.invoicePrefix': [REQUIRED_MESSAGE, PREFIX_MESSAGE],
  'organization.nextInvoiceNumber': [REQUIRED_MESSAGE, NEXT_INVOICE_NUMBER_MESSAGE],
  'branch.name': [REQUIRED_MESSAGE, tooLongMessage(140)],
  'branch.code': [REQUIRED_MESSAGE, BRANCH_CODE_MESSAGE],
  'branch.phone': [tooLongMessage(PHONE_MAX_LENGTH), PHONE_INVALID_MESSAGE],
  'branch.email': [tooLongMessage(EMAIL_MAX_LENGTH), EMAIL_INVALID_MESSAGE],
  'branch.timezone': [TIMEZONE_MESSAGE],
  'branch.addressLine1': [REQUIRED_MESSAGE, tooLongMessage(180)],
  'branch.city': [REQUIRED_MESSAGE, tooLongMessage(100)],
  'branch.stateRegion': [tooLongMessage(100)],
  'branch.postalCode': [REQUIRED_MESSAGE, tooLongMessage(30)],
  'branch.countryCode': [COUNTRY_MESSAGE],
  'owner.firstName': [REQUIRED_MESSAGE, tooLongMessage(100)],
  'owner.lastName': [REQUIRED_MESSAGE, tooLongMessage(100)],
  'owner.email': [
    REQUIRED_MESSAGE,
    tooLongMessage(EMAIL_MAX_LENGTH),
    EMAIL_INVALID_MESSAGE,
    DUPLICATE_EMAIL_MESSAGE,
  ],
  'owner.phone': [tooLongMessage(PHONE_MAX_LENGTH), PHONE_INVALID_MESSAGE],
  'owner.password': [REQUIRED_MESSAGE, PASSWORD_LENGTH_MESSAGE, PASSWORD_EQUALS_EMAIL_MESSAGE],
  confirmPassword: [REQUIRED_MESSAGE, PASSWORDS_DONT_MATCH_MESSAGE],
};

// --- Shared per-rule validators (BR-25 order; first failing rule wins) ----

export function validateRequiredText(value: string, maxLength: number): string | null {
  const trimmed = value.trim();
  if (trimmed.length === 0) {
    return REQUIRED_MESSAGE;
  }
  return trimmed.length > maxLength ? tooLongMessage(maxLength) : null;
}

export function validateOptionalText(value: string, maxLength: number): string | null {
  return value.trim().length > maxLength ? tooLongMessage(maxLength) : null;
}

export function validateRequiredEmail(value: string): string | null {
  const trimmed = value.trim();
  if (trimmed.length === 0) {
    return REQUIRED_MESSAGE;
  }
  if (trimmed.length > EMAIL_MAX_LENGTH) {
    return tooLongMessage(EMAIL_MAX_LENGTH);
  }
  return isValidEmailFormat(trimmed) ? null : EMAIL_INVALID_MESSAGE;
}

export function validateOptionalEmail(value: string): string | null {
  const trimmed = value.trim();
  if (trimmed.length === 0) {
    return null;
  }
  if (trimmed.length > EMAIL_MAX_LENGTH) {
    return tooLongMessage(EMAIL_MAX_LENGTH);
  }
  return isValidEmailFormat(trimmed) ? null : EMAIL_INVALID_MESSAGE;
}

export function validateRequiredPhone(value: string): string | null {
  const trimmed = value.trim();
  if (trimmed.length === 0) {
    return REQUIRED_MESSAGE;
  }
  if (trimmed.length > PHONE_MAX_LENGTH) {
    return tooLongMessage(PHONE_MAX_LENGTH);
  }
  return isValidPhoneFormat(trimmed) ? null : PHONE_INVALID_MESSAGE;
}

export function validateOptionalPhone(value: string): string | null {
  const trimmed = value.trim();
  if (trimmed.length === 0) {
    return null;
  }
  if (trimmed.length > PHONE_MAX_LENGTH) {
    return tooLongMessage(PHONE_MAX_LENGTH);
  }
  return isValidPhoneFormat(trimmed) ? null : PHONE_INVALID_MESSAGE;
}

export function validateTimezone(value: string): string | null {
  const trimmed = value.trim();
  return trimmed.length > 0 && SUPPORTED_TIME_ZONES.has(trimmed) ? null : TIMEZONE_MESSAGE;
}

export function validateCurrency(value: string): string | null {
  const trimmed = value.trim().toUpperCase();
  return trimmed.length > 0 && SUPPORTED_CURRENCIES.has(trimmed) ? null : CURRENCY_MESSAGE;
}

export function validateCountryCode(value: string): string | null {
  const trimmed = value.trim().toUpperCase();
  return trimmed.length > 0 && SUPPORTED_COUNTRIES.has(trimmed) ? null : COUNTRY_MESSAGE;
}

export function validateTaxRate(value: number | null): string | null {
  if (value === null) {
    return REQUIRED_MESSAGE;
  }
  if (!Number.isFinite(value) || value < 0 || value > 100) {
    return TAX_RATE_MESSAGE;
  }
  const rounded = Math.round(value * 10_000) / 10_000;
  return Math.abs(rounded - value) < 1e-9 ? null : TAX_RATE_MESSAGE;
}

export function validatePrefix(value: string): string | null {
  const trimmed = value.trim();
  if (trimmed.length === 0) {
    return REQUIRED_MESSAGE;
  }
  const upper = trimmed.toUpperCase();
  return upper.length <= 20 && /^[A-Z0-9-]+$/.test(upper) ? null : PREFIX_MESSAGE;
}

export function validateNextInvoiceNumber(value: number | null): string | null {
  if (value === null) {
    return REQUIRED_MESSAGE;
  }
  return Number.isInteger(value) && value >= 1 && value <= 999_999_999_999
    ? null
    : NEXT_INVOICE_NUMBER_MESSAGE;
}

export function validateBranchCode(value: string): string | null {
  const trimmed = value.trim();
  if (trimmed.length === 0) {
    return REQUIRED_MESSAGE;
  }
  const upper = trimmed.toUpperCase();
  return upper.length <= 8 && /^[A-Z0-9-]+$/.test(upper) ? null : BRANCH_CODE_MESSAGE;
}

/** Validates one open day's start time. Closed days are never validated. */
export function validateBusinessHoursStart(start: string): string | null {
  return start.length === 0 ? REQUIRED_MESSAGE : null;
}

/** Validates one open day's end time against its start. Closed days are never validated. */
export function validateBusinessHoursEnd(start: string, end: string): string | null {
  if (end.length === 0) {
    return REQUIRED_MESSAGE;
  }
  return end > start ? null : END_NOT_AFTER_START_MESSAGE;
}

export function validatePassword(value: string, ownerEmail: string): string | null {
  if (value.length === 0) {
    return REQUIRED_MESSAGE;
  }
  if (value.length < 12 || value.length > 128) {
    return PASSWORD_LENGTH_MESSAGE;
  }
  return value.toLowerCase() === ownerEmail.trim().toLowerCase()
    ? PASSWORD_EQUALS_EMAIL_MESSAGE
    : null;
}

export function validateConfirmPassword(value: string, password: string): string | null {
  if (value.length === 0) {
    return REQUIRED_MESSAGE;
  }
  return value === password ? null : PASSWORDS_DONT_MATCH_MESSAGE;
}

function isValidEmailFormat(email: string): boolean {
  if (email.length === 0 || /\s/.test(email)) {
    return false;
  }
  const at = email.indexOf('@');
  if (at <= 0 || email.indexOf('@', at + 1) >= 0) {
    return false;
  }
  return email.slice(at + 1).includes('.');
}

function isValidPhoneFormat(phone: string): boolean {
  if (/[^0-9 +().-]/.test(phone)) {
    return false;
  }
  return (phone.match(/\d/g)?.length ?? 0) >= 7;
}

// --- Central dispatcher (mirrors sign-in's validateField switch) ----------

/** First failing BR-25 rule for a simple (non business-hours) field, or `null` when valid. */
export function validateField(
  field: SimpleFieldKey,
  values: RegisterCompanyFormValue,
): string | null {
  switch (field) {
    case 'organization.name':
      return validateRequiredText(values.organization.name, 160);
    case 'organization.legalName':
      return validateRequiredText(values.organization.legalName, 200);
    case 'organization.taxId':
      return validateOptionalText(values.organization.taxId, 60);
    case 'organization.email':
      return validateRequiredEmail(values.organization.email);
    case 'organization.phone':
      return validateRequiredPhone(values.organization.phone);
    case 'organization.timezone':
      return validateTimezone(values.organization.timezone);
    case 'organization.currency':
      return validateCurrency(values.organization.currency);
    case 'organization.defaultTaxRate':
      return validateTaxRate(values.organization.defaultTaxRate);
    case 'organization.quotePrefix':
      return validatePrefix(values.organization.quotePrefix);
    case 'organization.workOrderPrefix':
      return validatePrefix(values.organization.workOrderPrefix);
    case 'organization.invoicePrefix':
      return validatePrefix(values.organization.invoicePrefix);
    case 'organization.nextInvoiceNumber':
      return validateNextInvoiceNumber(values.organization.nextInvoiceNumber);
    case 'branch.name':
      return validateRequiredText(values.branch.name, 140);
    case 'branch.code':
      return validateBranchCode(values.branch.code);
    case 'branch.phone':
      return validateOptionalPhone(values.branch.phone);
    case 'branch.email':
      return validateOptionalEmail(values.branch.email);
    case 'branch.timezone':
      return validateTimezone(values.branch.timezone);
    case 'branch.addressLine1':
      return validateRequiredText(values.branch.addressLine1, 180);
    case 'branch.city':
      return validateRequiredText(values.branch.city, 100);
    case 'branch.stateRegion':
      return validateOptionalText(values.branch.stateRegion, 100);
    case 'branch.postalCode':
      return validateRequiredText(values.branch.postalCode, 30);
    case 'branch.countryCode':
      return validateCountryCode(values.branch.countryCode);
    case 'owner.firstName':
      return validateRequiredText(values.owner.firstName, 100);
    case 'owner.lastName':
      return validateRequiredText(values.owner.lastName, 100);
    case 'owner.email':
      return validateRequiredEmail(values.owner.email);
    case 'owner.phone':
      return validateOptionalPhone(values.owner.phone);
    case 'owner.password':
      return validatePassword(values.owner.password, values.owner.email);
    case 'confirmPassword':
      return validateConfirmPassword(values.confirmPassword, values.owner.password);
  }
}

/** Every simple field key, in FR-01 section order (for empty-submit and full-form validation). */
export const SIMPLE_FIELD_KEYS: readonly SimpleFieldKey[] = [
  'organization.name',
  'organization.legalName',
  'organization.taxId',
  'organization.email',
  'organization.phone',
  'organization.timezone',
  'organization.currency',
  'organization.defaultTaxRate',
  'organization.quotePrefix',
  'organization.workOrderPrefix',
  'organization.invoicePrefix',
  'organization.nextInvoiceNumber',
  'branch.name',
  'branch.code',
  'branch.phone',
  'branch.email',
  'branch.timezone',
  'branch.addressLine1',
  'branch.city',
  'branch.stateRegion',
  'branch.postalCode',
  'branch.countryCode',
  'owner.firstName',
  'owner.lastName',
  'owner.email',
  'owner.phone',
  'owner.password',
  'confirmPassword',
];

export function businessHoursStartKey(day: Weekday): `branch.businessHours.${Weekday}.start` {
  return `branch.businessHours.${day}.start`;
}

export function businessHoursEndKey(day: Weekday): `branch.businessHours.${Weekday}.end` {
  return `branch.businessHours.${day}.end`;
}

/**
 * DOM id for a field's control: the dotted key with dots replaced by hyphens.
 * Accepts any dotted key string so `company-settings-and-branches` can reuse
 * it for its own (wider) field-key namespaces without duplicating this logic.
 */
export function fieldControlId(field: string): string {
  return field.replace(/\./g, '-');
}

/**
 * Maps server field errors (`400`/`409`) to displayed messages: only the
 * first message per known key, and any message outside its BR-24 allow-list
 * is replaced with the fallback (FR-13).
 */
export function mapServerFieldErrors(
  fieldErrors: Readonly<Record<string, readonly string[]>>,
): FieldErrors {
  const result: FieldErrors = {};

  for (const field of SIMPLE_FIELD_KEYS) {
    const message = fieldErrors[field]?.[0];
    if (message !== undefined) {
      result[field] = ALLOWED_MESSAGES[field].includes(message) ? message : FALLBACK_FIELD_MESSAGE;
    }
  }

  const weekdays: Weekday[] = [
    'monday',
    'tuesday',
    'wednesday',
    'thursday',
    'friday',
    'saturday',
    'sunday',
  ];
  for (const day of weekdays) {
    for (const part of ['start', 'end'] as const) {
      const key = `branch.businessHours.${day}.${part}` as const;
      const message = fieldErrors[key]?.[0];
      if (message !== undefined) {
        result[key] =
          message === REQUIRED_MESSAGE || message === END_NOT_AFTER_START_MESSAGE
            ? message
            : FALLBACK_FIELD_MESSAGE;
      }
    }
  }

  const rootMessage = fieldErrors['branch.businessHours']?.[0];
  if (rootMessage !== undefined) {
    result['branch.businessHours'] = FALLBACK_FIELD_MESSAGE;
  }

  return result;
}
