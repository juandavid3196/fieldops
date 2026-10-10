import {
  AvailabilityData,
  ContactData,
  PropertyData,
  ServiceData,
  StepErrors,
  WizardData,
  WizardStep,
} from './models/service-request.model';
import {
  CATEGORY_REQUIRED_MESSAGE,
  CONSENT_REQUIRED_MESSAGE,
  CONTACT_METHOD_MESSAGE,
  DATE_RANGE_MESSAGE,
  DATE_REQUIRED_MESSAGE,
  DESCRIPTION_REQUIRED_MESSAGE,
  EMAIL_INVALID_MESSAGE,
  FILE_COUNT_MESSAGE,
  FILE_EMPTY_MESSAGE,
  FILE_SIZE_MESSAGE,
  FILE_TOTAL_MESSAGE,
  FILE_TYPE_MESSAGE,
  PHONE_INVALID_MESSAGE,
  POSTAL_CODE_INVALID_MESSAGE,
  REQUIRED_MESSAGE,
  SERVICE_REQUIRED_MESSAGE,
  STATE_INVALID_MESSAGE,
  maxLengthMessage,
} from './service-request.messages';

export const MAX_FILES = 5;
export const MAX_FILE_BYTES = 10 * 1024 * 1024;
export const MAX_TOTAL_BYTES = 25 * 1024 * 1024;
export const MAX_ADVANCE_DAYS = 90;

// prettier-ignore
export const US_STATES: readonly string[] = [
  'AL', 'AK', 'AZ', 'AR', 'CA', 'CO', 'CT', 'DE', 'DC', 'FL', 'GA', 'HI', 'ID', 'IL', 'IN', 'IA',
  'KS', 'KY', 'LA', 'ME', 'MD', 'MA', 'MI', 'MN', 'MS', 'MO', 'MT', 'NE', 'NV', 'NH', 'NJ', 'NM',
  'NY', 'NC', 'ND', 'OH', 'OK', 'OR', 'PA', 'RI', 'SC', 'SD', 'TN', 'TX', 'UT', 'VT', 'VA', 'WA',
  'WV', 'WI', 'WY',
];

/** Field paths per step, in visual order (the first invalid field gets focus). */
export const STEP_FIELDS: Readonly<Record<WizardStep, readonly string[]>> = {
  contact: [
    'contact.firstName',
    'contact.lastName',
    'contact.email',
    'contact.phone',
    'contact.prefersEmail',
  ],
  property: [
    'property.propertyId',
    'property.addressLine1',
    'property.addressLine2',
    'property.city',
    'property.state',
    'property.postalCode',
    'property.accessInstructions',
  ],
  service: ['service.categoryId', 'service.serviceId', 'service.description', 'attachments'],
  availability: ['availability.preferredDate', 'availability.schedulingNotes'],
  review: ['consent'],
};

export const STEP_ORDER: readonly WizardStep[] = [
  'contact',
  'property',
  'service',
  'availability',
  'review',
];

export interface FileInfo {
  readonly name: string;
  readonly size: number;
  readonly type?: string;
}

/** DOM id of the control that owns a field path. */
export function fieldId(path: string): string {
  return `sr-${path.replace(/[^a-zA-Z0-9]+/g, '-').replace(/-$/, '')}`;
}

/** First field of the step with an error, in visual order. */
export function firstInvalidField(step: WizardStep, errors: StepErrors): string | null {
  return STEP_FIELDS[step].find((path) => errors[path] !== undefined) ?? null;
}

/** Step that owns a field path (`attachments[i]` belongs to Service details). */
export function stepOfField(path: string): WizardStep | null {
  const key = path.toLowerCase();
  if (key.startsWith('contact.')) return 'contact';
  if (key.startsWith('property.')) return 'property';
  if (key.startsWith('service.') || key.startsWith('attachments')) return 'service';
  if (key.startsWith('availability.')) return 'availability';
  return key === 'consent' ? 'review' : null;
}

export function validateContact(data: ContactData): StepErrors {
  const errors: Record<string, string> = {};
  requiredText(errors, 'contact.firstName', data.firstName, 100);
  requiredText(errors, 'contact.lastName', data.lastName, 100);

  const email = data.email.trim();
  if (email.length === 0) {
    errors['contact.email'] = REQUIRED_MESSAGE;
  } else if (!isValidEmail(email)) {
    errors['contact.email'] = EMAIL_INVALID_MESSAGE;
  }

  const phone = data.phone.trim();
  if (phone.length === 0) {
    errors['contact.phone'] = REQUIRED_MESSAGE;
  } else if (phone.length > 40 || !/^\d{10,15}$/.test(phone.replace(/[\s()\-+.]/g, ''))) {
    errors['contact.phone'] = PHONE_INVALID_MESSAGE;
  }

  if (!data.prefersEmail && !data.prefersSms) {
    errors['contact.prefersEmail'] = CONTACT_METHOD_MESSAGE;
  }
  return errors;
}

export function validateProperty(data: PropertyData): StepErrors {
  const errors: Record<string, string> = {};
  requiredText(errors, 'property.addressLine1', data.addressLine1, 180);
  optionalText(errors, 'property.addressLine2', data.addressLine2, 180);
  requiredText(errors, 'property.city', data.city, 100);
  if (!US_STATES.includes(data.state)) {
    errors['property.state'] = STATE_INVALID_MESSAGE;
  }
  if (!/^\d{5}(-\d{4})?$/.test(data.postalCode.trim())) {
    errors['property.postalCode'] = POSTAL_CODE_INVALID_MESSAGE;
  }
  optionalText(errors, 'property.accessInstructions', data.accessInstructions, 1000);
  return errors;
}

/** BR-05/BR-06 plus the attachment totals (BR-13). */
export function validateService(data: ServiceData, files: readonly FileInfo[] = []): StepErrors {
  const errors: Record<string, string> = {};
  if (data.categoryId === '') {
    errors['service.categoryId'] = CATEGORY_REQUIRED_MESSAGE;
  }
  if (!data.notSure && data.serviceId === '') {
    errors['service.serviceId'] = SERVICE_REQUIRED_MESSAGE;
  }
  const description = data.description.trim();
  if (description.length === 0) {
    errors['service.description'] = DESCRIPTION_REQUIRED_MESSAGE;
  } else if (description.length > 1000) {
    errors['service.description'] = maxLengthMessage(1000);
  }
  const total = files.reduce((sum, file) => sum + file.size, 0);
  if (files.length > MAX_FILES || total > MAX_TOTAL_BYTES) {
    errors['attachments'] = FILE_TOTAL_MESSAGE('Your files');
  }
  return errors;
}

/** `today` is the organization's local date as `YYYY-MM-DD`. */
export function validateAvailability(data: AvailabilityData, today: string): StepErrors {
  const errors: Record<string, string> = {};
  if (data.dateMode === 'date') {
    if (data.preferredDate === '') {
      errors['availability.preferredDate'] = DATE_REQUIRED_MESSAGE;
    } else if (!isDateInRange(data.preferredDate, today)) {
      errors['availability.preferredDate'] = DATE_RANGE_MESSAGE;
    }
  }
  optionalText(errors, 'availability.schedulingNotes', data.schedulingNotes, 1000);
  return errors;
}

export function validateConsent(consent: boolean): StepErrors {
  return consent ? {} : { consent: CONSENT_REQUIRED_MESSAGE };
}

export function validateStep(
  step: WizardStep,
  data: WizardData,
  context: {
    readonly files: readonly FileInfo[];
    readonly today: string;
    readonly consent: boolean;
  },
): StepErrors {
  switch (step) {
    case 'contact':
      return validateContact(data.contact);
    case 'property':
      return validateProperty(data.property);
    case 'service':
      return validateService(data.service, context.files);
    case 'availability':
      return validateAvailability(data.availability, context.today);
    case 'review':
      return validateConsent(context.consent);
  }
}

const ALLOWED_TYPES: Readonly<Record<string, readonly string[]>> = {
  jpg: ['image/jpeg'],
  jpeg: ['image/jpeg'],
  png: ['image/png'],
  pdf: ['application/pdf'],
};

/** Reason message when `candidate` cannot join the accepted files, or `null` (BR-13). */
export function validateNewFile(accepted: readonly FileInfo[], candidate: FileInfo): string | null {
  const extension = candidate.name.includes('.')
    ? (candidate.name.split('.').pop() ?? '').toLowerCase()
    : '';
  const mimes = ALLOWED_TYPES[extension];
  if (mimes === undefined || (candidate.type && !mimes.includes(candidate.type))) {
    return FILE_TYPE_MESSAGE(candidate.name);
  }
  if (candidate.size === 0) {
    return FILE_EMPTY_MESSAGE(candidate.name);
  }
  if (candidate.size > MAX_FILE_BYTES) {
    return FILE_SIZE_MESSAGE(candidate.name);
  }
  if (accepted.length >= MAX_FILES) {
    return FILE_COUNT_MESSAGE(candidate.name);
  }
  const total = accepted.reduce((sum, file) => sum + file.size, 0);
  return total + candidate.size > MAX_TOTAL_BYTES ? FILE_TOTAL_MESSAGE(candidate.name) : null;
}

/** Local `YYYY-MM-DD` of `now` in `timeZone` (falls back to the browser zone). */
export function todayInTimeZone(timeZone: string | undefined, now: Date = new Date()): string {
  const options: Intl.DateTimeFormatOptions = { year: 'numeric', month: '2-digit', day: '2-digit' };
  let formatter: Intl.DateTimeFormat;
  try {
    formatter = new Intl.DateTimeFormat('en-US', { ...options, timeZone });
  } catch {
    formatter = new Intl.DateTimeFormat('en-US', options);
  }
  const parts = Object.fromEntries(formatter.formatToParts(now).map((p) => [p.type, p.value]));
  return `${parts['year']}-${parts['month']}-${parts['day']}`;
}

/** Adds calendar days to a `YYYY-MM-DD` date without time-zone conversion. */
export function addDays(date: string, days: number): string {
  const [year, month, day] = date.split('-').map(Number);
  const result = new Date(Date.UTC(year, month - 1, day + days));
  return [
    result.getUTCFullYear(),
    String(result.getUTCMonth() + 1).padStart(2, '0'),
    String(result.getUTCDate()).padStart(2, '0'),
  ].join('-');
}

function isDateInRange(value: string, today: string): boolean {
  return (
    /^\d{4}-\d{2}-\d{2}$/.test(value) &&
    addDays(value, 0) === value &&
    value >= today &&
    value <= addDays(today, MAX_ADVANCE_DAYS)
  );
}

function requiredText(errors: Record<string, string>, path: string, value: string, max: number) {
  const length = value.trim().length;
  if (length === 0) {
    errors[path] = REQUIRED_MESSAGE;
  } else if (length > max) {
    errors[path] = maxLengthMessage(max);
  }
}

function optionalText(errors: Record<string, string>, path: string, value: string, max: number) {
  if (value.trim().length > max) {
    errors[path] = maxLengthMessage(max);
  }
}

function isValidEmail(email: string): boolean {
  if (email.length > 254 || /\s/.test(email)) {
    return false;
  }
  const at = email.indexOf('@');
  if (at <= 0 || email.indexOf('@', at + 1) >= 0) {
    return false;
  }
  return email.slice(at + 1).includes('.');
}
