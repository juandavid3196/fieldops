import {
  EMAIL_INVALID_MESSAGE,
  FALLBACK_FIELD_MESSAGE,
  NEXT_INVOICE_NUMBER_FLOOR_MESSAGE,
  NEXT_INVOICE_NUMBER_MESSAGE,
  PHONE_INVALID_MESSAGE,
  PREFIX_MESSAGE,
  REQUIRED_MESSAGE,
  TAX_RATE_MESSAGE,
  TIMEZONE_MESSAGE,
  CURRENCY_MESSAGE,
  tooLongMessage,
} from '../register-company/register-company.messages';
import {
  EMAIL_MAX_LENGTH,
  PHONE_MAX_LENGTH,
  validateCurrency,
  validateNextInvoiceNumber,
  validateOptionalText,
  validatePrefix,
  validateRequiredEmail,
  validateRequiredPhone,
  validateRequiredText,
  validateTaxRate,
  validateTimezone,
} from '../register-company/register-company.validators';
import { OrganizationFieldErrors, OrganizationFieldKey } from '../../models/company-settings.model';
import { OrganizationSettingsRequest } from '../../models/company-settings.model';

/** Raw value of the reused `FormGroup<OrganizationFormControls>` (flat BR-01 fields). */
export interface OrganizationFormValue {
  name: string;
  legalName: string;
  taxId: string;
  email: string;
  phone: string;
  timezone: string;
  currency: string;
  defaultTaxRate: number | null;
  quotePrefix: string;
  workOrderPrefix: string;
  invoicePrefix: string;
  nextInvoiceNumber: number | null;
}

/** Every organization field, in Company profile / Taxes / Document numbering order. */
export const ORGANIZATION_FIELD_KEYS: readonly OrganizationFieldKey[] = [
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
];

/** Server request key for each field (the API body is flat, unlike the `organization.*` UI keys). */
const REQUEST_KEYS: Readonly<Record<OrganizationFieldKey, keyof OrganizationFormValue>> = {
  'organization.name': 'name',
  'organization.legalName': 'legalName',
  'organization.taxId': 'taxId',
  'organization.email': 'email',
  'organization.phone': 'phone',
  'organization.timezone': 'timezone',
  'organization.currency': 'currency',
  'organization.defaultTaxRate': 'defaultTaxRate',
  'organization.quotePrefix': 'quotePrefix',
  'organization.workOrderPrefix': 'workOrderPrefix',
  'organization.invoicePrefix': 'invoicePrefix',
  'organization.nextInvoiceNumber': 'nextInvoiceNumber',
};

/** The BR-13 catalog messages a field is allowed to show; anything else becomes the fallback. */
const ALLOWED_MESSAGES: Readonly<Record<OrganizationFieldKey, readonly string[]>> = {
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
  'organization.nextInvoiceNumber': [
    REQUIRED_MESSAGE,
    NEXT_INVOICE_NUMBER_MESSAGE,
    NEXT_INVOICE_NUMBER_FLOOR_MESSAGE,
  ],
};

/** First failing BR-01 rule for one field, or `null` when valid (client mirror; BR-02 is server-only). */
export function validateOrganizationField(
  field: OrganizationFieldKey,
  value: OrganizationFormValue,
): string | null {
  switch (field) {
    case 'organization.name':
      return validateRequiredText(value.name, 160);
    case 'organization.legalName':
      return validateRequiredText(value.legalName, 200);
    case 'organization.taxId':
      return validateOptionalText(value.taxId, 60);
    case 'organization.email':
      return validateRequiredEmail(value.email);
    case 'organization.phone':
      return validateRequiredPhone(value.phone);
    case 'organization.timezone':
      return validateTimezone(value.timezone);
    case 'organization.currency':
      return validateCurrency(value.currency);
    case 'organization.defaultTaxRate':
      return validateTaxRate(value.defaultTaxRate);
    case 'organization.quotePrefix':
      return validatePrefix(value.quotePrefix);
    case 'organization.workOrderPrefix':
      return validatePrefix(value.workOrderPrefix);
    case 'organization.invoicePrefix':
      return validatePrefix(value.invoicePrefix);
    case 'organization.nextInvoiceNumber':
      return validateNextInvoiceNumber(value.nextInvoiceNumber);
  }
}

/** Builds the `PUT /organization-settings` body (BR-01 normalization) from the raw form value. */
export function buildOrganizationSettingsRequest(
  value: OrganizationFormValue,
  updatedAt: string,
): OrganizationSettingsRequest {
  return {
    name: value.name.trim(),
    legalName: value.legalName.trim(),
    taxId: value.taxId.trim(),
    email: value.email.trim().toLowerCase(),
    phone: value.phone.trim(),
    timezone: value.timezone.trim(),
    currency: value.currency.trim().toUpperCase(),
    defaultTaxRate: value.defaultTaxRate ?? 0,
    quotePrefix: value.quotePrefix.trim().toUpperCase(),
    workOrderPrefix: value.workOrderPrefix.trim().toUpperCase(),
    invoicePrefix: value.invoicePrefix.trim().toUpperCase(),
    nextInvoiceNumber: value.nextInvoiceNumber ?? 1,
    updatedAt,
  };
}

/**
 * Maps the flat `PUT /organization-settings` field errors (`400`/`409`) to the
 * `organization.*` UI keys the reused sections expect (BR-13): only the first
 * message per known key, and any message outside its allow-list becomes the
 * fallback. `updatedAt` (BR-07) has no UI field and is dropped; its `409`/`400`
 * is shown through the page-level message instead.
 */
export function mapOrganizationServerFieldErrors(
  fieldErrors: Readonly<Record<string, readonly string[]>>,
): OrganizationFieldErrors {
  const result: OrganizationFieldErrors = {};

  for (const field of ORGANIZATION_FIELD_KEYS) {
    const message = fieldErrors[REQUEST_KEYS[field]]?.[0];
    if (message !== undefined) {
      result[field] = ALLOWED_MESSAGES[field].includes(message) ? message : FALLBACK_FIELD_MESSAGE;
    }
  }

  return result;
}
