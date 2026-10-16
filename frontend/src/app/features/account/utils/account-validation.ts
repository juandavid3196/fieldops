import {
  PASSWORD_EQUALS_EMAIL_MESSAGE,
  PASSWORD_LENGTH_MESSAGE,
  PASSWORDS_DONT_MATCH_MESSAGE,
  REQUIRED_MESSAGE,
  tooLongMessage,
} from '../../organizations/pages/register-company/register-company.messages';
import { validateOptionalPhone } from '../../organizations/pages/register-company/register-company.validators';

export const NAME_MAX_LENGTH = 100;
export const PASSWORD_MIN_LENGTH = 12;
export const PASSWORD_MAX_LENGTH = 128;
export const PASSWORD_EQUALS_CURRENT_MESSAGE =
  'Choose a password that is different from your current password.';

export type ProfileField = 'firstName' | 'lastName' | 'phone';
export type ProfileErrors = Partial<Record<ProfileField, string>>;

/** BR-01 client mirror (trimmed values, first failing rule wins). */
export function validateProfile(value: Readonly<Record<ProfileField, string>>): ProfileErrors {
  const errors: ProfileErrors = {};
  for (const field of ['firstName', 'lastName'] as const) {
    const trimmed = value[field].trim();
    if (trimmed.length === 0) {
      errors[field] = REQUIRED_MESSAGE;
    } else if (trimmed.length > NAME_MAX_LENGTH) {
      errors[field] = tooLongMessage(NAME_MAX_LENGTH);
    }
  }
  const phone = validateOptionalPhone(value.phone);
  if (phone !== null) {
    errors.phone = phone;
  }
  return errors;
}

export type PasswordField = 'currentPassword' | 'newPassword' | 'confirmPassword';
export type PasswordErrors = Partial<Record<PasswordField, string>>;

export interface PasswordValues {
  readonly currentPassword: string;
  readonly newPassword: string;
  readonly confirmPassword: string;
}

export interface PasswordRequirement {
  readonly label: string;
  readonly met: boolean;
}

/** BR-02 requirements panel: an empty new password meets nothing. Passwords are never trimmed. */
export function passwordRequirements(
  values: Pick<PasswordValues, 'currentPassword' | 'newPassword'>,
  email: string,
): readonly PasswordRequirement[] {
  const value = values.newPassword;
  const typed = value.length > 0;
  return [
    {
      label: '12–128 characters',
      met: value.length >= PASSWORD_MIN_LENGTH && value.length <= PASSWORD_MAX_LENGTH,
    },
    {
      label: 'Must not match your email',
      met: typed && value.toLowerCase() !== email.toLowerCase(),
    },
    {
      label: 'Different from your current password',
      met: typed && value !== values.currentPassword,
    },
  ];
}

/** BR-02 client mirror; one message per field, first failing rule wins. */
export function validatePassword(values: PasswordValues, email: string): PasswordErrors {
  const errors: PasswordErrors = {};
  const { currentPassword, newPassword, confirmPassword } = values;

  if (currentPassword.length === 0) {
    errors.currentPassword = REQUIRED_MESSAGE;
  } else if (currentPassword.length > PASSWORD_MAX_LENGTH) {
    errors.currentPassword = tooLongMessage(PASSWORD_MAX_LENGTH);
  }

  if (newPassword.length === 0) {
    errors.newPassword = REQUIRED_MESSAGE;
  } else if (newPassword.length < PASSWORD_MIN_LENGTH || newPassword.length > PASSWORD_MAX_LENGTH) {
    errors.newPassword = PASSWORD_LENGTH_MESSAGE;
  } else if (newPassword.toLowerCase() === email.toLowerCase()) {
    errors.newPassword = PASSWORD_EQUALS_EMAIL_MESSAGE;
  } else if (newPassword === currentPassword) {
    errors.newPassword = PASSWORD_EQUALS_CURRENT_MESSAGE;
  }

  if (confirmPassword.length === 0) {
    errors.confirmPassword = REQUIRED_MESSAGE;
  } else if (confirmPassword !== newPassword) {
    errors.confirmPassword = PASSWORDS_DONT_MATCH_MESSAGE;
  }
  return errors;
}

/** BR-10: "Too many attempts. Try again in {n} minutes." (`n` rounded up, never below 1). */
export function tooManyAttemptsMessage(retryAfterSeconds: number): string {
  const minutes = Math.max(1, Math.ceil(retryAfterSeconds / 60));
  return `Too many attempts. Try again in ${minutes} ${minutes === 1 ? 'minute' : 'minutes'}.`;
}

export const PHOTO_MAX_BYTES = 5_242_880;
export const PHOTO_SIZE_MESSAGE = 'Choose a file of 5 MB or smaller.';
export const PHOTO_TYPE_MESSAGE = 'Choose a JPG or PNG file.';
export const PHOTO_HELPER = 'JPG or PNG. Max 5 MB.';
export const PHOTO_ACCEPT = '.png,.jpg,.jpeg,image/png,image/jpeg';

const PNG_SIGNATURE = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
const JPEG_SIGNATURE = [0xff, 0xd8, 0xff];

function startsWith(bytes: Uint8Array, signature: readonly number[]): boolean {
  return signature.every((byte, index) => bytes[index] === byte);
}

/**
 * Client mirror of BR-03: size, then the type read from the file content and, when the browser
 * declares one, matching it. The server still decides; this only avoids a pointless upload.
 */
export async function validatePhotoFile(file: File): Promise<string | null> {
  if (file.size > PHOTO_MAX_BYTES) {
    return PHOTO_SIZE_MESSAGE;
  }
  const head = new Uint8Array(await file.slice(0, PNG_SIGNATURE.length).arrayBuffer());
  const detected = startsWith(head, PNG_SIGNATURE)
    ? 'image/png'
    : startsWith(head, JPEG_SIGNATURE)
      ? 'image/jpeg'
      : null;
  const declared = file.type.toLowerCase();
  return detected === null || (declared !== '' && declared !== detected)
    ? PHOTO_TYPE_MESSAGE
    : null;
}
