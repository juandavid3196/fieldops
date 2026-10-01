import { US_STATE_OPTIONS } from '../../../organizations/data/us-states';
import {
  CUSTOMER_FIELD_KEYS,
  CustomerFieldErrors,
  CustomerFieldKey,
  CustomerType,
  MAX_TAGS,
} from '../../models/customer.model';
import { normalizeEmail, normalizePhone } from '../../utils/customer-format';

export const FIELD_LABELS: Readonly<Record<CustomerFieldKey, string>> = {
  type: 'Customer type',
  companyName: 'Company name',
  firstName: 'First name',
  lastName: 'Last name',
  title: 'Title',
  email: 'Email',
  phone: 'Mobile phone',
  preferences: 'Preferred communication',
  addressLine1: 'Address line 1',
  city: 'City',
  stateRegion: 'State',
  postalCode: 'ZIP code',
  serviceInstructions: 'Service instructions',
  branchId: 'Branch',
  tagIds: 'Tags',
  internalNote: 'Internal note',
};

export interface CustomerFormValue {
  readonly type: CustomerType | null;
  readonly companyName: string;
  readonly firstName: string;
  readonly lastName: string;
  readonly title: string;
  readonly email: string;
  readonly phone: string;
  readonly prefersEmail: boolean;
  readonly prefersSms: boolean;
  readonly addressLine1: string;
  readonly city: string;
  readonly stateRegion: string;
  readonly postalCode: string;
  readonly serviceInstructions: string;
  readonly branchId: string | null;
  readonly tagIds: readonly string[];
  readonly internalNote: string;
}

const STATE_CODES = new Set(US_STATE_OPTIONS.map((option) => option.code));
const US_ZIP = /^\d{5}(-\d{4})?$/;

function required(value: string, requiredMessage: string, max: number, maxMessage: string) {
  const text = value.trim();
  if (text.length === 0) {
    return requiredMessage;
  }
  return text.length > max ? maxMessage : null;
}

const within = (value: string, max: number, message: string): string | null =>
  value.trim().length > max ? message : null;

/** BR-10 / BR-12 / BR-13 message for one field, or `null` when valid. `usesStateList`: US organization. */
export function validateCustomerField(
  field: CustomerFieldKey,
  value: CustomerFormValue,
  usesStateList: boolean,
): string | null {
  const commercial = value.type === 'commercial';
  switch (field) {
    case 'type':
      return value.type === null ? 'Choose a customer type.' : null;
    case 'companyName':
      return commercial
        ? required(value.companyName, 'Enter a company name.', 180, 'Use 180 characters or fewer.')
        : null;
    case 'firstName':
      return required(value.firstName, 'Enter a first name.', 100, 'Use 100 characters or fewer.');
    case 'lastName': {
      const message = required(
        value.lastName,
        'Enter a last name.',
        100,
        'Use 100 characters or fewer.',
      );
      if (message !== null) {
        return message;
      }
      return !commercial && `${value.firstName.trim()} ${value.lastName.trim()}`.length > 180
        ? 'Use 180 characters or fewer for the full name.'
        : null;
    }
    case 'title':
      return commercial ? within(value.title, 100, 'Use 100 characters or fewer.') : null;
    case 'email': {
      if (value.email.trim().length === 0) {
        return 'Enter an email.';
      }
      return normalizeEmail(value.email) === null ? 'Enter a valid email.' : null;
    }
    case 'phone':
      return value.phone.trim().length > 0 && normalizePhone(value.phone) === null
        ? 'Enter a valid phone number.'
        : null;
    case 'preferences':
      if (!value.prefersEmail && !value.prefersSms) {
        return 'Choose at least one communication method.';
      }
      return value.prefersSms && normalizePhone(value.phone) === null
        ? 'Add a mobile phone to use SMS.'
        : null;
    case 'addressLine1':
      return required(value.addressLine1, 'Enter an address.', 180, 'Use 180 characters or fewer.');
    case 'city':
      return required(value.city, 'Enter a city.', 100, 'Use 100 characters or fewer.');
    case 'stateRegion': {
      const state = value.stateRegion.trim();
      if (state.length === 0) {
        return null;
      }
      return (usesStateList ? !STATE_CODES.has(state) : state.length > 100)
        ? 'Choose a valid state.'
        : null;
    }
    case 'postalCode': {
      const zip = value.postalCode.trim();
      if (zip.length === 0) {
        return null;
      }
      return (usesStateList ? !US_ZIP.test(zip) : zip.length > 30)
        ? 'Enter a valid ZIP code.'
        : null;
    }
    case 'serviceInstructions':
      return within(value.serviceInstructions, 2000, 'Use 2,000 characters or fewer.');
    case 'branchId':
      return value.branchId === null || value.branchId === '' ? 'Choose a branch.' : null;
    case 'tagIds':
      return value.tagIds.length > MAX_TAGS ? 'Choose up to 10 tags.' : null;
    case 'internalNote':
      return within(value.internalNote, 2000, 'Use 2,000 characters or fewer.');
  }
}

export function validateCustomer(
  value: CustomerFormValue,
  usesStateList: boolean,
): CustomerFieldErrors {
  const errors: Record<string, string> = {};
  for (const field of CUSTOMER_FIELD_KEYS) {
    const message = validateCustomerField(field, value, usesStateList);
    if (message !== null) {
      errors[field] = message;
    }
  }
  return errors;
}

const SERVER_KEYS: Readonly<Record<string, CustomerFieldKey>> = {
  prefersemail: 'preferences',
  preferssms: 'preferences',
  preferences: 'preferences',
  preferredcommunication: 'preferences',
  tagids: 'tagIds',
};

/** Maps ProblemDetails `errors` keys (`contact.email`, `Email`, `property.city`...) to drawer fields. */
export function mapServerFieldErrors(
  fieldErrors: Readonly<Record<string, readonly string[]>>,
): CustomerFieldErrors {
  const mapped: Record<string, string> = {};
  for (const [key, messages] of Object.entries(fieldErrors)) {
    const last = (key.split('.').pop() ?? key).replace(/\[\d+\]$/, '').toLowerCase();
    const field =
      SERVER_KEYS[last] ??
      CUSTOMER_FIELD_KEYS.find((candidate) => candidate.toLowerCase() === last);
    if (field !== undefined && messages[0] !== undefined && mapped[field] === undefined) {
      mapped[field] = messages[0];
    }
  }
  return mapped;
}
