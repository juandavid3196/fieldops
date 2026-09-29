import { timezoneGenericName } from '../../data/display-names';
import {
  FieldKey,
  RegisterCompanyFormValue,
  WEEKDAYS,
  Weekday,
} from '../../models/organization-registration.model';
import { validateNextInvoiceNumber } from './register-company.validators';

export type WizardStep = 1 | 2 | 3 | 4;
export type FormStep = 1 | 2 | 3;

export const STEP_LABELS: Readonly<Record<WizardStep, string>> = {
  1: 'Company & branch',
  2: 'Business settings',
  3: 'Owner account',
  4: 'Review',
};

export const NOT_PROVIDED = 'Not provided';
const DASH = '—';

type Hours = RegisterCompanyFormValue['branch']['businessHours'];

const BUSINESS_SETTINGS_ORGANIZATION_KEYS: readonly string[] = [
  'organization.currency',
  'organization.defaultTaxRate',
  'organization.quotePrefix',
  'organization.workOrderPrefix',
  'organization.invoicePrefix',
  'organization.nextInvoiceNumber',
];

/** BR-02: the wizard step that contains a field key. */
export function stepOfField(key: FieldKey): FormStep {
  if (key.startsWith('branch.businessHours') || BUSINESS_SETTINGS_ORGANIZATION_KEYS.includes(key)) {
    return 2;
  }
  return key.startsWith('owner.') || key === 'confirmPassword' ? 3 : 1;
}

// --- BR-04 time options ---------------------------------------------------

/** "8:00 AM" style display of an `HH:mm` value. */
export function formatTime(value: string): string {
  const [hours, minutes] = value.split(':').map(Number);
  const suffix = hours < 12 ? 'AM' : 'PM';
  const hour12 = hours % 12 === 0 ? 12 : hours % 12;
  return `${hour12}:${String(minutes).padStart(2, '0')} ${suffix}`;
}

export interface TimeOption {
  readonly code: string;
  readonly label: string;
}

/** 48 half-hour options, `00:00` to `23:30`, submitted as `HH:mm`. */
export const TIME_OPTIONS: TimeOption[] = Array.from({ length: 48 }, (_, index) => {
  const code = `${String(Math.floor(index / 2)).padStart(2, '0')}:${index % 2 === 0 ? '00' : '30'}`;
  return { code, label: formatTime(code) };
});

// --- FR-05 Copy Monday ------------------------------------------------------

/** Copies Monday's open state, start and end to Tuesday-Friday; weekend is unchanged. */
export function copyMondayToWeekdays(hours: Hours): Hours {
  const monday = hours.monday;
  const copy = (): Hours[Weekday] => ({ open: monday.open, start: monday.start, end: monday.end });
  return {
    ...hours,
    tuesday: copy(),
    wednesday: copy(),
    thursday: copy(),
    friday: copy(),
  };
}

// --- BR-05 business hours summary ------------------------------------------

const DAY_ABBREVIATIONS: Readonly<Record<Weekday, string>> = {
  monday: 'Mon',
  tuesday: 'Tue',
  wednesday: 'Wed',
  thursday: 'Thu',
  friday: 'Fri',
  saturday: 'Sat',
  sunday: 'Sun',
};

export function summarizeBusinessHours(hours: Hours, timezone: string): string {
  const groups: { first: Weekday; last: Weekday; start: string; end: string }[] = [];
  let previous: Weekday | null = null;

  for (const day of WEEKDAYS) {
    const value = hours[day];
    if (!value.open) {
      previous = null;
      continue;
    }
    const last = groups[groups.length - 1];
    if (previous !== null && last.start === value.start && last.end === value.end) {
      last.last = day;
    } else {
      groups.push({ first: day, last: day, start: value.start, end: value.end });
    }
    previous = day;
  }

  if (groups.length === 0) {
    return 'Closed all week';
  }

  const text = groups
    .map((group) => {
      const days =
        group.first === group.last
          ? DAY_ABBREVIATIONS[group.first]
          : `${DAY_ABBREVIATIONS[group.first]} – ${DAY_ABBREVIATIONS[group.last]}`;
      return `${days}, ${formatTime(group.start)} – ${formatTime(group.end)}`;
    })
    .join(' · ');
  return `${text} (${timezoneGenericName(timezone)})`;
}

// --- BR-06 numbering examples ----------------------------------------------

export function numberingExamples(
  organization: Pick<
    RegisterCompanyFormValue['organization'],
    'quotePrefix' | 'workOrderPrefix' | 'invoicePrefix' | 'nextInvoiceNumber'
  >,
): string {
  const prefix = (value: string): string => value.trim().toUpperCase() || DASH;
  const next =
    validateNextInvoiceNumber(organization.nextInvoiceNumber) === null
      ? String(organization.nextInvoiceNumber)
      : DASH;
  return `Examples: ${prefix(organization.quotePrefix)}-1 · ${prefix(organization.workOrderPrefix)}-1 · ${prefix(organization.invoicePrefix)}-${next}`;
}

// --- BR-07 password requirements -------------------------------------------

export interface PasswordRequirements {
  readonly lengthMet: boolean;
  readonly differsFromEmailMet: boolean;
}

export function passwordRequirements(password: string, ownerEmail: string): PasswordRequirements {
  return {
    lengthMet: password.length >= 12 && password.length <= 128,
    differsFromEmailMet:
      password.length > 0 && password.toLowerCase() !== ownerEmail.trim().toLowerCase(),
  };
}

// --- BR-09 review formatting -----------------------------------------------

export function orNotProvided(value: string): string {
  const trimmed = value.trim();
  return trimmed.length > 0 ? trimmed : NOT_PROVIDED;
}

/** "{City}, {State / Region} {Postal code}"; without a state "{City} {Postal code}". */
export function cityLine(city: string, stateRegion: string, postalCode: string): string {
  const state = stateRegion.trim();
  const place = state.length > 0 ? `${city.trim()}, ${state}` : city.trim();
  return `${place} ${postalCode.trim()}`;
}

export function ownerInitials(firstName: string, lastName: string): string {
  return `${firstName.trim().charAt(0)}${lastName.trim().charAt(0)}`.toUpperCase();
}
