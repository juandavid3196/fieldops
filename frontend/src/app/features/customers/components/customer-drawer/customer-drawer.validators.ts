import { US_STATE_OPTIONS } from '../../../organizations/data/us-states';
import {
  CUSTOMER_FIELD_KEYS,
  CustomerFieldErrors,
  CustomerFieldKey,
  CustomerType,
  MAX_TAGS,
} from '../../models/customer.model';
import { normalizeEmail, normalizePhone } from '../../utils/customer-format';

import { localized, t } from '../../../../core/i18n/translate';
export const FIELD_LABELS: Readonly<Record<CustomerFieldKey, string>> = localized(() => ({
  type: t('customers.customerDrawerValidators.customerType'),
  companyName: t('customers.customerDrawerValidators.companyName'),
  firstName: t('customers.customerDrawerValidators.firstName'),
  lastName: t('customers.customerDrawerValidators.lastName'),
  title: t('customers.customerDrawerValidators.title'),
  email: t('common.email'),
  phone: t('customers.customerDrawerValidators.mobilePhone'),
  preferences: t('customers.customerDrawerValidators.preferredCommunication'),
  addressLine1: t('customers.customerDrawerValidators.addressLine1'),
  city: t('customers.customerDrawerValidators.city'),
  stateRegion: t('customers.customerDrawerValidators.state'),
  postalCode: t('customers.customerDrawerValidators.zipCode'),
  serviceInstructions: t('customers.customerDrawerValidators.serviceInstructions'),
  branchId: t('customers.customerDrawerValidators.branch'),
  tagIds: t('customers.customerDrawerValidators.tags'),
  internalNote: t('customers.customerDrawerValidators.internalNote'),
}));

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
      return value.type === null
        ? t('customers.customerDrawerValidators.chooseACustomerType')
        : null;
    case 'companyName':
      return commercial
        ? required(
            value.companyName,
            t('customers.customerDrawerValidators.enterACompanyName'),
            180,
            t('customers.customerDrawerValidators.use180CharactersOr'),
          )
        : null;
    case 'firstName':
      return required(
        value.firstName,
        t('customers.customerDrawerValidators.enterAFirstName'),
        100,
        t('customers.customerDrawerValidators.use100CharactersOr'),
      );
    case 'lastName': {
      const message = required(
        value.lastName,
        t('customers.customerDrawerValidators.enterALastName'),
        100,
        t('customers.customerDrawerValidators.use100CharactersOr'),
      );
      if (message !== null) {
        return message;
      }
      return !commercial && `${value.firstName.trim()} ${value.lastName.trim()}`.length > 180
        ? t('customers.customerDrawerValidators.use180CharactersOr2')
        : null;
    }
    case 'title':
      return commercial
        ? within(value.title, 100, t('customers.customerDrawerValidators.use100CharactersOr'))
        : null;
    case 'email': {
      if (value.email.trim().length === 0) {
        return t('customers.customerDrawerValidators.enterAnEmail');
      }
      return normalizeEmail(value.email) === null
        ? t('customers.customerDrawerValidators.enterAValidEmail')
        : null;
    }
    case 'phone':
      return value.phone.trim().length > 0 && normalizePhone(value.phone) === null
        ? t('customers.customerDrawerValidators.enterAValidPhone')
        : null;
    case 'preferences':
      if (!value.prefersEmail && !value.prefersSms) {
        return t('customers.customerDrawerValidators.chooseAtLeastOne');
      }
      return value.prefersSms && normalizePhone(value.phone) === null
        ? t('customers.customerDrawerValidators.addAMobilePhone')
        : null;
    case 'addressLine1':
      return required(
        value.addressLine1,
        t('customers.customerDrawerValidators.enterAnAddress'),
        180,
        t('customers.customerDrawerValidators.use180CharactersOr'),
      );
    case 'city':
      return required(
        value.city,
        t('customers.customerDrawerValidators.enterACity'),
        100,
        t('customers.customerDrawerValidators.use100CharactersOr'),
      );
    case 'stateRegion': {
      const state = value.stateRegion.trim();
      if (state.length === 0) {
        return null;
      }
      return (usesStateList ? !STATE_CODES.has(state) : state.length > 100)
        ? t('customers.customerDrawerValidators.chooseAValidState')
        : null;
    }
    case 'postalCode': {
      const zip = value.postalCode.trim();
      if (zip.length === 0) {
        return null;
      }
      return (usesStateList ? !US_ZIP.test(zip) : zip.length > 30)
        ? t('customers.customerDrawerValidators.enterAValidZip')
        : null;
    }
    case 'serviceInstructions':
      return within(
        value.serviceInstructions,
        2000,
        t('customers.customerDrawerValidators.use2000Characters'),
      );
    case 'branchId':
      return value.branchId === null || value.branchId === ''
        ? t('customers.customerDrawerValidators.chooseABranch')
        : null;
    case 'tagIds':
      return value.tagIds.length > MAX_TAGS
        ? t('customers.customerDrawerValidators.chooseUpTo10')
        : null;
    case 'internalNote':
      return within(
        value.internalNote,
        2000,
        t('customers.customerDrawerValidators.use2000Characters'),
      );
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
