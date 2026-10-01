import {
  TECHNICIAN_FIELD_KEYS,
  TechnicianFieldErrors,
  TechnicianFieldKey,
} from '../../models/team.model';

export interface TechnicianFormValue {
  readonly firstName: string;
  readonly lastName: string;
  readonly email: string;
  readonly phone: string;
  readonly employeeCode: string;
  readonly branchId: string | null;
  readonly notes: string;
}

export const FIELD_LABELS: Readonly<Record<TechnicianFieldKey, string>> = {
  firstName: 'First name',
  lastName: 'Last name',
  email: 'Email',
  phone: 'Mobile phone',
  employeeCode: 'Profile ID',
  branchId: 'Home branch',
  notes: 'Notes',
};

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const PHONE_CHARACTERS = /^[0-9\s+()\-.]*$/;
const CODE = /^[A-Za-z0-9_-]+$/;

export const EMPTY_FORM: TechnicianFormValue = {
  firstName: '',
  lastName: '',
  email: '',
  phone: '',
  employeeCode: '',
  branchId: null,
  notes: '',
};

/** Field validation table of the spec; returns the message or null. */
export function validateTechnicianField(
  field: TechnicianFieldKey,
  value: TechnicianFormValue,
  branchIds: readonly string[],
): string | null {
  switch (field) {
    case 'firstName':
    case 'lastName': {
      const text = value[field].trim();
      if (text.length === 0) {
        return field === 'firstName' ? 'Enter a first name.' : 'Enter a last name.';
      }
      return text.length > 100 ? 'Use 100 characters or fewer.' : null;
    }
    case 'email': {
      const text = value.email.trim();
      return text.length === 0 || (text.length <= 254 && EMAIL.test(text))
        ? null
        : 'Enter a valid email address.';
    }
    case 'phone': {
      const text = value.phone.trim();
      if (text.length === 0) {
        return null;
      }
      const digits = text.replace(/\D/g, '').length;
      return text.length <= 40 && PHONE_CHARACTERS.test(text) && digits >= 7 && digits <= 15
        ? null
        : 'Enter a valid phone number.';
    }
    case 'employeeCode': {
      const text = value.employeeCode.trim();
      return text.length === 0 || (text.length <= 50 && CODE.test(text))
        ? null
        : 'Use letters, numbers, hyphens or underscores.';
    }
    case 'branchId':
      return value.branchId !== null && branchIds.includes(value.branchId)
        ? null
        : 'Choose a branch you have access to.';
    case 'notes':
      return value.notes.trim().length > 2000 ? 'Use 2000 characters or fewer.' : null;
  }
}

export function validateTechnician(
  value: TechnicianFormValue,
  branchIds: readonly string[],
): TechnicianFieldErrors {
  const errors: Record<string, string> = {};
  for (const field of TECHNICIAN_FIELD_KEYS) {
    const message = validateTechnicianField(field, value, branchIds);
    if (message !== null) {
      errors[field] = message;
    }
  }
  return errors;
}

const SERVER_KEYS: Readonly<Record<string, TechnicianFieldKey>> = {
  employeecode: 'employeeCode',
  profileid: 'employeeCode',
  branchid: 'branchId',
};

/** Maps `errors.<field>` of a 400 to form fields (first message each). */
export function mapServerFieldErrors(
  fieldErrors: Readonly<Record<string, readonly string[]>>,
): TechnicianFieldErrors {
  const mapped: Record<string, string> = {};
  for (const [key, messages] of Object.entries(fieldErrors)) {
    const last = (key.split('.').pop() ?? key).toLowerCase();
    const field =
      SERVER_KEYS[last] ??
      TECHNICIAN_FIELD_KEYS.find((candidate) => candidate.toLowerCase() === last);
    if (field !== undefined && messages[0] !== undefined && mapped[field] === undefined) {
      mapped[field] = messages[0];
    }
  }
  return mapped;
}
