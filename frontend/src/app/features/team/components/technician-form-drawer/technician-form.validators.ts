import {
  TECHNICIAN_FIELD_KEYS,
  TechnicianFieldErrors,
  TechnicianFieldKey,
} from '../../models/team.model';

import { localized, t } from '../../../../core/i18n/translate';
export interface TechnicianFormValue {
  readonly firstName: string;
  readonly lastName: string;
  readonly email: string;
  readonly phone: string;
  readonly employeeCode: string;
  readonly branchId: string | null;
  readonly notes: string;
}

export const FIELD_LABELS: Readonly<Record<TechnicianFieldKey, string>> = localized(() => ({
  firstName: t('team.technicianFormValidators.firstName'),
  lastName: t('team.technicianFormValidators.lastName'),
  email: t('common.email'),
  phone: t('team.technicianFormValidators.mobilePhone'),
  employeeCode: t('team.technicianFormValidators.profileId'),
  branchId: t('team.technicianFormValidators.homeBranch'),
  notes: t('common.notes'),
}));

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
        return field === 'firstName'
          ? t('team.technicianFormValidators.enterAFirstName')
          : t('team.technicianFormValidators.enterALastName');
      }
      return text.length > 100 ? t('team.technicianFormValidators.use100CharactersOr') : null;
    }
    case 'email': {
      const text = value.email.trim();
      return text.length === 0 || (text.length <= 254 && EMAIL.test(text))
        ? null
        : t('team.technicianFormValidators.enterAValidEmail');
    }
    case 'phone': {
      const text = value.phone.trim();
      if (text.length === 0) {
        return null;
      }
      const digits = text.replace(/\D/g, '').length;
      return text.length <= 40 && PHONE_CHARACTERS.test(text) && digits >= 7 && digits <= 15
        ? null
        : t('team.technicianFormValidators.enterAValidPhone');
    }
    case 'employeeCode': {
      const text = value.employeeCode.trim();
      return text.length === 0 || (text.length <= 50 && CODE.test(text))
        ? null
        : t('team.technicianFormValidators.useLettersNumbersHyphens');
    }
    case 'branchId':
      return value.branchId !== null && branchIds.includes(value.branchId)
        ? null
        : t('team.technicianFormValidators.chooseABranchYou');
    case 'notes':
      return value.notes.trim().length > 2000
        ? t('team.technicianFormValidators.use2000CharactersOr')
        : null;
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
