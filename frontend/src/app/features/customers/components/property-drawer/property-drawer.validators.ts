import { US_STATE_OPTIONS } from '../../../organizations/data/us-states';

export const PROPERTY_FIELD_KEYS = [
  'name',
  'addressLine1',
  'addressLine2',
  'city',
  'stateRegion',
  'postalCode',
  'branchId',
  'serviceInstructions',
] as const;
export type PropertyFieldKey = (typeof PROPERTY_FIELD_KEYS)[number];
export type PropertyFieldErrors = Partial<Record<PropertyFieldKey, string>>;

export const PROPERTY_FIELD_LABELS: Readonly<Record<PropertyFieldKey, string>> = {
  name: 'Property name',
  addressLine1: 'Address line 1',
  addressLine2: 'Address line 2',
  city: 'City',
  stateRegion: 'State',
  postalCode: 'ZIP code',
  branchId: 'Branch',
  serviceInstructions: 'Service instructions',
};

export interface PropertyFormValue {
  readonly name: string;
  readonly addressLine1: string;
  readonly addressLine2: string;
  readonly city: string;
  readonly stateRegion: string;
  readonly postalCode: string;
  readonly branchId: string | null;
  readonly serviceInstructions: string;
}

const STATE_CODES = new Set(US_STATE_OPTIONS.map((option) => option.code));
const US_ZIP = /^\d{5}(-\d{4})?$/;
const FEW_180 = 'Use 180 characters or fewer.';

function required(value: string, requiredMessage: string, max: number, maxMessage: string) {
  const text = value.trim();
  if (text.length === 0) {
    return requiredMessage;
  }
  return text.length > max ? maxMessage : null;
}

const within = (value: string, max: number, message: string): string | null =>
  value.trim().length > max ? message : null;

/** BR-05 / BR-06 message for one property field, or `null` when valid. */
export function validatePropertyField(
  field: PropertyFieldKey,
  value: PropertyFormValue,
  usesStateList: boolean,
): string | null {
  switch (field) {
    case 'name':
      return required(value.name, 'Enter a property name.', 140, 'Use 140 characters or fewer.');
    case 'addressLine1':
      return required(value.addressLine1, 'Enter an address.', 180, FEW_180);
    case 'addressLine2':
      return within(value.addressLine2, 180, FEW_180);
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
    case 'branchId':
      return value.branchId === null || value.branchId === '' ? 'Choose a branch.' : null;
    case 'serviceInstructions':
      return within(value.serviceInstructions, 2000, 'Use 2,000 characters or fewer.');
  }
}

export function validateProperty(
  value: PropertyFormValue,
  usesStateList: boolean,
): PropertyFieldErrors {
  const errors: Record<string, string> = {};
  for (const field of PROPERTY_FIELD_KEYS) {
    const message = validatePropertyField(field, value, usesStateList);
    if (message !== null) {
      errors[field] = message;
    }
  }
  return errors;
}

/** Maps ProblemDetails `errors` keys (`branchId`, `BranchId`, `property.name`...) to drawer fields. */
export function mapPropertyServerErrors(
  fieldErrors: Readonly<Record<string, readonly string[]>>,
): PropertyFieldErrors {
  const mapped: Record<string, string> = {};
  for (const [key, messages] of Object.entries(fieldErrors)) {
    const last = (key.split('.').pop() ?? key).toLowerCase();
    const field = PROPERTY_FIELD_KEYS.find((candidate) => candidate.toLowerCase() === last);
    if (field !== undefined && messages[0] !== undefined && mapped[field] === undefined) {
      mapped[field] = messages[0];
    }
  }
  return mapped;
}
