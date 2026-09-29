import { US_STATE_CODES } from '../../data/us-states';
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
  COUNTRY_MESSAGE,
  CURRENCY_MESSAGE,
  tooLongMessage,
} from '../register-company/register-company.messages';
import {
  EMAIL_MAX_LENGTH,
  PHONE_MAX_LENGTH,
  validateCountryCode,
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
import {
  OrganizationFieldErrors,
  OrganizationFieldKey,
  OrganizationSettingsRequest,
} from '../../models/company-settings.model';

export const WEBSITE_INVALID_MESSAGE = 'Enter a valid website.';
export const STATE_MESSAGE = 'Select a state.';
export const NEXT_QUOTE_NUMBER_FLOOR_MESSAGE = 'Enter a number greater than the last quote number.';
export const NEXT_WORK_ORDER_NUMBER_FLOOR_MESSAGE =
  'Enter a number greater than the last work order number.';

/** Raw value of the Company setup `FormGroup<CompanySetupFormControls>`. */
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
  website: string;
  addressLine1: string;
  city: string;
  stateRegion: string;
  postalCode: string;
  countryCode: string;
  pricesIncludeTax: boolean;
  nextQuoteNumber: number | null;
  nextWorkOrderNumber: number | null;
}

/** Every organization field, in page order (profile, taxes, numbering); drives summary order. */
export const ORGANIZATION_FIELD_KEYS: readonly OrganizationFieldKey[] = [
  'organization.legalName',
  'organization.name',
  'organization.website',
  'organization.email',
  'organization.phone',
  'organization.taxId',
  'organization.addressLine1',
  'organization.city',
  'organization.stateRegion',
  'organization.postalCode',
  'organization.countryCode',
  'organization.timezone',
  'organization.currency',
  'organization.defaultTaxRate',
  'organization.quotePrefix',
  'organization.workOrderPrefix',
  'organization.invoicePrefix',
  'organization.nextInvoiceNumber',
  'organization.nextQuoteNumber',
  'organization.nextWorkOrderNumber',
];

/** Visible labels (handoff copy), shared by field labels and the error summary. */
export const ORGANIZATION_FIELD_LABELS: Readonly<Record<OrganizationFieldKey, string>> = {
  'organization.name': 'Display name',
  'organization.legalName': 'Legal business name',
  'organization.website': 'Website',
  'organization.email': 'Business email',
  'organization.phone': 'Phone number',
  'organization.taxId': 'Tax ID (EIN)',
  'organization.addressLine1': 'Street address',
  'organization.city': 'City',
  'organization.stateRegion': 'State',
  'organization.postalCode': 'ZIP code',
  'organization.countryCode': 'Default country',
  'organization.timezone': 'Default time zone',
  'organization.currency': 'Currency',
  'organization.defaultTaxRate': 'Default sales tax rate',
  'organization.quotePrefix': 'Quote prefix',
  'organization.workOrderPrefix': 'Work order prefix',
  'organization.invoicePrefix': 'Invoice prefix',
  'organization.nextInvoiceNumber': 'Next invoice number',
  'organization.nextQuoteNumber': 'Next quote number',
  'organization.nextWorkOrderNumber': 'Next work order number',
};

/** Server request key for each field (the API body is flat, unlike the `organization.*` UI keys). */
const REQUEST_KEYS: Readonly<Record<OrganizationFieldKey, keyof OrganizationFormValue>> = {
  'organization.name': 'name',
  'organization.legalName': 'legalName',
  'organization.website': 'website',
  'organization.email': 'email',
  'organization.phone': 'phone',
  'organization.taxId': 'taxId',
  'organization.addressLine1': 'addressLine1',
  'organization.city': 'city',
  'organization.stateRegion': 'stateRegion',
  'organization.postalCode': 'postalCode',
  'organization.countryCode': 'countryCode',
  'organization.timezone': 'timezone',
  'organization.currency': 'currency',
  'organization.defaultTaxRate': 'defaultTaxRate',
  'organization.quotePrefix': 'quotePrefix',
  'organization.workOrderPrefix': 'workOrderPrefix',
  'organization.invoicePrefix': 'invoicePrefix',
  'organization.nextInvoiceNumber': 'nextInvoiceNumber',
  'organization.nextQuoteNumber': 'nextQuoteNumber',
  'organization.nextWorkOrderNumber': 'nextWorkOrderNumber',
};

/** The catalog messages a field is allowed to show; anything else becomes the fallback. */
const ALLOWED_MESSAGES: Readonly<Record<OrganizationFieldKey, readonly string[]>> = {
  'organization.name': [REQUIRED_MESSAGE, tooLongMessage(160)],
  'organization.legalName': [REQUIRED_MESSAGE, tooLongMessage(200)],
  'organization.website': [WEBSITE_INVALID_MESSAGE, tooLongMessage(255)],
  'organization.email': [REQUIRED_MESSAGE, tooLongMessage(EMAIL_MAX_LENGTH), EMAIL_INVALID_MESSAGE],
  'organization.phone': [REQUIRED_MESSAGE, tooLongMessage(PHONE_MAX_LENGTH), PHONE_INVALID_MESSAGE],
  'organization.taxId': [tooLongMessage(60)],
  'organization.addressLine1': [REQUIRED_MESSAGE, tooLongMessage(180)],
  'organization.city': [REQUIRED_MESSAGE, tooLongMessage(100)],
  'organization.stateRegion': [STATE_MESSAGE, tooLongMessage(100)],
  'organization.postalCode': [REQUIRED_MESSAGE, tooLongMessage(30)],
  'organization.countryCode': [COUNTRY_MESSAGE],
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
  'organization.nextQuoteNumber': [
    REQUIRED_MESSAGE,
    NEXT_INVOICE_NUMBER_MESSAGE,
    NEXT_QUOTE_NUMBER_FLOOR_MESSAGE,
  ],
  'organization.nextWorkOrderNumber': [
    REQUIRED_MESSAGE,
    NEXT_INVOICE_NUMBER_MESSAGE,
    NEXT_WORK_ORDER_NUMBER_FLOOR_MESSAGE,
  ],
};

/** The keys edited in the "Edit sequences" dialog. */
export const SEQUENCE_FIELD_KEYS: readonly OrganizationFieldKey[] = [
  'organization.nextQuoteNumber',
  'organization.nextWorkOrderNumber',
  'organization.nextInvoiceNumber',
];

/** BR-04: optional, at most 255, `https?://` optional, a host with a dot and no spaces. */
export function validateWebsite(value: string): string | null {
  const trimmed = value.trim();
  if (trimmed.length === 0) {
    return null;
  }
  if (trimmed.length > 255) {
    return tooLongMessage(255);
  }
  if (/\s/.test(trimmed)) {
    return WEBSITE_INVALID_MESSAGE;
  }
  const withoutProtocol = trimmed.replace(/^https?:\/\//i, '');
  const host = withoutProtocol.split(/[/?#]/, 1)[0];
  const validHost = host.includes('.') && !host.startsWith('.') && !host.endsWith('.');
  return validHost ? null : WEBSITE_INVALID_MESSAGE;
}

/** BR-04: required, one of the 50 states or DC, when the country is the US; otherwise optional. */
export function validateStateRegion(state: string, countryCode: string): string | null {
  if (countryCode.trim().toUpperCase() === 'US') {
    return US_STATE_CODES.has(state.trim().toUpperCase()) ? null : STATE_MESSAGE;
  }
  return validateOptionalText(state, 100);
}

/** First failing rule for one field, or `null` when valid (client mirror; server floors excluded). */
export function validateOrganizationField(
  field: OrganizationFieldKey,
  value: OrganizationFormValue,
): string | null {
  switch (field) {
    case 'organization.name':
      return validateRequiredText(value.name, 160);
    case 'organization.legalName':
      return validateRequiredText(value.legalName, 200);
    case 'organization.website':
      return validateWebsite(value.website);
    case 'organization.email':
      return validateRequiredEmail(value.email);
    case 'organization.phone':
      return validateRequiredPhone(value.phone);
    case 'organization.taxId':
      return validateOptionalText(value.taxId, 60);
    case 'organization.addressLine1':
      return validateRequiredText(value.addressLine1, 180);
    case 'organization.city':
      return validateRequiredText(value.city, 100);
    case 'organization.stateRegion':
      return validateStateRegion(value.stateRegion, value.countryCode);
    case 'organization.postalCode':
      return validateRequiredText(value.postalCode, 30);
    case 'organization.countryCode':
      return validateCountryCode(value.countryCode);
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
    case 'organization.nextQuoteNumber':
      return validateNextInvoiceNumber(value.nextQuoteNumber);
    case 'organization.nextWorkOrderNumber':
      return validateNextInvoiceNumber(value.nextWorkOrderNumber);
  }
}

/** Builds the `PUT /organization-settings` body (normalized) from the raw form value. */
export function buildOrganizationSettingsRequest(
  value: OrganizationFormValue,
  updatedAt: string,
  confirmCurrencyChange = false,
): OrganizationSettingsRequest {
  const countryCode = value.countryCode.trim().toUpperCase();
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
    website: value.website.trim(),
    addressLine1: value.addressLine1.trim(),
    city: value.city.trim(),
    stateRegion:
      countryCode === 'US' ? value.stateRegion.trim().toUpperCase() : value.stateRegion.trim(),
    postalCode: value.postalCode.trim(),
    countryCode,
    pricesIncludeTax: value.pricesIncludeTax,
    nextQuoteNumber: value.nextQuoteNumber ?? 1,
    nextWorkOrderNumber: value.nextWorkOrderNumber ?? 1,
    ...(confirmCurrencyChange ? { confirmCurrencyChange: true } : {}),
    updatedAt,
  };
}

/**
 * Maps the flat `PUT /organization-settings` field errors (`400`/`409`) to the
 * `organization.*` UI keys: only the first message per known key, and any
 * message outside its allow-list becomes the fallback. `updatedAt` and
 * `currency` conflicts have no field message of their own here (the page
 * opens the currency confirmation instead).
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
