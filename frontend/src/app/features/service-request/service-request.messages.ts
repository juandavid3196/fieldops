export const LOAD_ERROR_MESSAGE = "We couldn't load this form.";
export const UNAVAILABLE_MESSAGE = "This request form isn't available.";
export const SUBMIT_ERROR_MESSAGE = "We couldn't submit your request. Please try again.";
export const RATE_LIMITED_MESSAGE = 'Too many requests. Please try again later.';
export const ATTACHMENT_SIZE_MESSAGE =
  'Your attachments are too large to send. Remove some files and try again.';
export const SERVER_FIELD_MESSAGE = 'Check this field and try again.';
export const SERVER_ATTACHMENTS_MESSAGE = "One or more files can't be accepted.";

export const REQUIRED_MESSAGE = 'This field is required.';
export const EMAIL_INVALID_MESSAGE = 'Enter a valid email address.';
export const PHONE_INVALID_MESSAGE = 'Enter a phone number with 10 to 15 digits.';
export const CONTACT_METHOD_MESSAGE = 'Select at least one method.';
export const STATE_INVALID_MESSAGE = 'Select a state.';
export const POSTAL_CODE_INVALID_MESSAGE = 'Enter a ZIP code like 78704 or 78704-1234.';
export const CATEGORY_REQUIRED_MESSAGE = 'Select a type of service.';
export const SERVICE_REQUIRED_MESSAGE = 'Select a service or choose "I\'m not sure".';
export const DESCRIPTION_REQUIRED_MESSAGE = 'Describe the problem.';
export const DATE_REQUIRED_MESSAGE = 'Choose a date.';
export const DATE_RANGE_MESSAGE = 'Choose a date from today up to 90 days from now.';
export const CONSENT_REQUIRED_MESSAGE = 'Confirm these details to submit your request.';

export function maxLengthMessage(max: number): string {
  return `Use ${max} characters or fewer.`;
}

export const FILE_COUNT_MESSAGE = (name: string): string =>
  `${name} was not added: you can add up to 5 files.`;
export const FILE_TYPE_MESSAGE = (name: string): string =>
  `${name} was not added: only JPG, PNG or PDF files are allowed.`;
export const FILE_EMPTY_MESSAGE = (name: string): string =>
  `${name} was not added: the file is empty.`;
export const FILE_SIZE_MESSAGE = (name: string): string =>
  `${name} was not added: files can be at most 10 MB.`;
export const FILE_TOTAL_MESSAGE = (name: string): string =>
  `${name} was not added: files can total at most 25 MB.`;

export const URGENCY_LABELS = {
  standard: 'Standard',
  urgent: 'Urgent',
  emergency: 'Emergency',
} as const;

export const TIME_WINDOW_LABELS = {
  morning: 'Morning (8 AM - 12 PM)',
  afternoon: 'Afternoon (12 PM - 5 PM)',
  evening: 'Evening (5 PM - 8 PM)',
  any: 'Any time (8 AM - 8 PM)',
} as const;

export const NOT_SURE_LABEL = "I'm not sure";

/** Portal wizard (BR-28): a known property must be chosen. */
export const PORTAL_PROPERTY_REQUIRED_MESSAGE = 'Select one of your properties.';
