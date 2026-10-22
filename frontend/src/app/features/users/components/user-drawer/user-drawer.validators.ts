import { InviteFieldErrors, InviteFieldKey, INVITE_FIELD_KEYS } from '../../models/users.model';

import { localized, t } from '../../../../core/i18n/translate';
export const EMAIL_INVALID_MESSAGE = (): string =>
  t('users.userDrawerValidators.emailInvalidMessage');
export const FIRST_NAME_MESSAGE = (): string => t('users.userDrawerValidators.firstNameMessage');
export const LAST_NAME_MESSAGE = (): string => t('users.userDrawerValidators.lastNameMessage');
export const ROLE_MESSAGE = (): string => t('users.userDrawerValidators.roleMessage');
export const BRANCH_REQUIRED_MESSAGE = (): string =>
  t('users.userDrawerValidators.branchRequiredMessage');
export const EXPIRY_MESSAGE = (): string => t('users.userDrawerValidators.expiryMessage');
export const EXPIRY_OPTIONS = [3, 7, 14] as const;
export const FIELD_LABELS: Readonly<Record<InviteFieldKey, string>> = localized(() => ({
  email: t('users.userDrawerValidators.emailAddress'),
  firstName: t('users.userDrawerValidators.firstName'),
  lastName: t('users.userDrawerValidators.lastName'),
  roleCode: t('common.role'),
  branchIds: t('users.userDrawerValidators.branchAccess'),
  expiresInDays: t('users.userDrawerValidators.invitationExpiresIn'),
}));

export interface InviteFormValue {
  readonly email: string;
  readonly firstName: string;
  readonly lastName: string;
  readonly roleCode: string;
  readonly isAllBranches: boolean;
  readonly branchIds: readonly string[];
  readonly linkTeamProfile: boolean;
  readonly expiresInDays: number | null;
}

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/** BR-08 client mirror for one field; `null` when valid. */
export function validateInviteField(
  field: InviteFieldKey,
  value: InviteFormValue,
  validRoleCodes: readonly string[],
): string | null {
  switch (field) {
    case 'email': {
      const email = value.email.trim();
      return email.length > 0 && email.length <= 254 && EMAIL_PATTERN.test(email)
        ? null
        : EMAIL_INVALID_MESSAGE();
    }
    case 'firstName': {
      const name = value.firstName.trim();
      return name.length > 0 && name.length <= 100 ? null : FIRST_NAME_MESSAGE();
    }
    case 'lastName': {
      const name = value.lastName.trim();
      return name.length > 0 && name.length <= 100 ? null : LAST_NAME_MESSAGE();
    }
    case 'roleCode':
      return validRoleCodes.includes(value.roleCode) ? null : ROLE_MESSAGE();
    case 'branchIds':
      return value.isAllBranches || value.branchIds.length > 0 ? null : BRANCH_REQUIRED_MESSAGE();
    case 'expiresInDays':
      return (EXPIRY_OPTIONS as readonly number[]).includes(value.expiresInDays ?? -1)
        ? null
        : EXPIRY_MESSAGE();
  }
}

/** Access-only validation for edit mode (role and branches). */
export function validateAccessFields(
  value: InviteFormValue,
  validRoleCodes: readonly string[],
): InviteFieldErrors {
  return validateFields(['roleCode', 'branchIds'], value, validRoleCodes);
}

export function validateFields(
  fields: readonly InviteFieldKey[],
  value: InviteFormValue,
  validRoleCodes: readonly string[],
): InviteFieldErrors {
  const errors: InviteFieldErrors = {};
  for (const field of fields) {
    const message = validateInviteField(field, value, validRoleCodes);
    if (message !== null) {
      errors[field] = message;
    }
  }
  return errors;
}

/** Maps server `errors` keys (`email`, `branchIds`, ...) to drawer fields; unknown keys are dropped. */
export function mapServerFieldErrors(
  fieldErrors: Readonly<Record<string, readonly string[]>>,
): InviteFieldErrors {
  const mapped: InviteFieldErrors = {};
  for (const key of INVITE_FIELD_KEYS) {
    const message = fieldErrors[key]?.[0];
    if (message !== undefined) {
      mapped[key] = message;
    }
  }
  return mapped;
}
