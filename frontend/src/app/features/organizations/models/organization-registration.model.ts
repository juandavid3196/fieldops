import { FormControl, FormGroup } from '@angular/forms';

export type Weekday =
  'monday' | 'tuesday' | 'wednesday' | 'thursday' | 'friday' | 'saturday' | 'sunday';

export const WEEKDAYS: readonly Weekday[] = [
  'monday',
  'tuesday',
  'wednesday',
  'thursday',
  'friday',
  'saturday',
  'sunday',
];

export const WEEKDAY_LABELS: Readonly<Record<Weekday, string>> = {
  monday: 'Monday',
  tuesday: 'Tuesday',
  wednesday: 'Wednesday',
  thursday: 'Thursday',
  friday: 'Friday',
  saturday: 'Saturday',
  sunday: 'Sunday',
};

/** `{ "start": "HH:mm", "end": "HH:mm" }` for one open day (BR-15). */
export interface BusinessHoursDay {
  readonly start: string;
  readonly end: string;
}

/** Open days only; a closed day has no key. All days closed is `{}` (AC-30). */
export type BusinessHoursPayload = Partial<Record<Weekday, BusinessHoursDay>>;

/**
 * Body of `POST /organization-registrations` (BR-01). No organization,
 * branch, user or role identifier field exists anywhere in this shape
 * (FR-05); `confirmPassword` never appears here (it exists only in the
 * browser, per BR-01).
 */
export interface OrganizationRegistrationRequest {
  readonly organization: {
    readonly name: string;
    readonly legalName: string;
    readonly taxId: string;
    readonly email: string;
    readonly phone: string;
    readonly timezone: string;
    readonly currency: string;
    readonly defaultTaxRate: number;
    readonly quotePrefix: string;
    readonly workOrderPrefix: string;
    readonly invoicePrefix: string;
    readonly nextInvoiceNumber: number;
  };
  readonly branch: {
    readonly name: string;
    readonly code: string;
    readonly phone: string;
    readonly email: string;
    readonly timezone: string;
    readonly addressLine1: string;
    readonly city: string;
    readonly stateRegion: string;
    readonly postalCode: string;
    readonly countryCode: string;
    readonly businessHours: BusinessHoursPayload;
  };
  readonly owner: {
    readonly firstName: string;
    readonly lastName: string;
    readonly email: string;
    readonly password: string;
    readonly phone: string;
  };
}

/** Body of the `201` response. */
export interface OrganizationRegistrationResponse {
  readonly organizationId: string;
}

// --- Reactive Forms shapes (browser-only; never sent as-is) ---------------

export interface BusinessHoursDayControls {
  open: FormControl<boolean>;
  start: FormControl<string>;
  end: FormControl<string>;
}

export type BusinessHoursControls = Record<Weekday, FormGroup<BusinessHoursDayControls>>;

export interface OrganizationFormControls {
  name: FormControl<string>;
  legalName: FormControl<string>;
  taxId: FormControl<string>;
  email: FormControl<string>;
  phone: FormControl<string>;
  timezone: FormControl<string>;
  currency: FormControl<string>;
  defaultTaxRate: FormControl<number | null>;
  quotePrefix: FormControl<string>;
  workOrderPrefix: FormControl<string>;
  invoicePrefix: FormControl<string>;
  nextInvoiceNumber: FormControl<number | null>;
}

export interface BranchFormControls {
  name: FormControl<string>;
  code: FormControl<string>;
  phone: FormControl<string>;
  email: FormControl<string>;
  timezone: FormControl<string>;
  addressLine1: FormControl<string>;
  city: FormControl<string>;
  stateRegion: FormControl<string>;
  postalCode: FormControl<string>;
  countryCode: FormControl<string>;
  businessHours: FormGroup<BusinessHoursControls>;
}

export interface OwnerFormControls {
  firstName: FormControl<string>;
  lastName: FormControl<string>;
  email: FormControl<string>;
  phone: FormControl<string>;
  password: FormControl<string>;
}

export interface RegisterCompanyFormControls {
  organization: FormGroup<OrganizationFormControls>;
  branch: FormGroup<BranchFormControls>;
  owner: FormGroup<OwnerFormControls>;
  confirmPassword: FormControl<string>;
}

/** Raw value shape of `RegisterCompanyFormControls` (`form.getRawValue()`). */
export interface RegisterCompanyFormValue {
  organization: {
    name: string;
    legalName: string;
    taxId: string;
    email: string;
    phone: string;
    timezone: string;
    currency: string;
    defaultTaxRate: number | null;
    quotePrefix: string;
    workOrderPrefix: string;
    invoicePrefix: string;
    nextInvoiceNumber: number | null;
  };
  branch: {
    name: string;
    code: string;
    phone: string;
    email: string;
    timezone: string;
    addressLine1: string;
    city: string;
    stateRegion: string;
    postalCode: string;
    countryCode: string;
    businessHours: Record<Weekday, { open: boolean; start: string; end: string }>;
  };
  owner: {
    firstName: string;
    lastName: string;
    email: string;
    phone: string;
    password: string;
  };
  confirmPassword: string;
}

/** Every leaf field the form validates against the BR-25 table, excluding business hours. */
export type SimpleFieldKey =
  | 'organization.name'
  | 'organization.legalName'
  | 'organization.taxId'
  | 'organization.email'
  | 'organization.phone'
  | 'organization.timezone'
  | 'organization.currency'
  | 'organization.defaultTaxRate'
  | 'organization.quotePrefix'
  | 'organization.workOrderPrefix'
  | 'organization.invoicePrefix'
  | 'organization.nextInvoiceNumber'
  | 'branch.name'
  | 'branch.code'
  | 'branch.phone'
  | 'branch.email'
  | 'branch.timezone'
  | 'branch.addressLine1'
  | 'branch.city'
  | 'branch.stateRegion'
  | 'branch.postalCode'
  | 'branch.countryCode'
  | 'owner.firstName'
  | 'owner.lastName'
  | 'owner.email'
  | 'owner.phone'
  | 'owner.password'
  | 'confirmPassword';

/** A day's start/end time field key (`branch.businessHours.{day}.start`/`.end`). */
export type BusinessHoursFieldKey = `branch.businessHours.${Weekday}.${'start' | 'end'}`;

/** The whole-object business hours error key (server-only structural failure). */
export type BusinessHoursRootKey = 'branch.businessHours';

export type FieldKey = SimpleFieldKey | BusinessHoursFieldKey | BusinessHoursRootKey;

/** Displayed field messages; a field is absent when valid. */
export type FieldErrors = Partial<Record<FieldKey, string>>;

/** Builds the BR-01 request body from the raw form value (FR-15, AC-30). */
export function buildRegistrationRequest(
  value: RegisterCompanyFormValue,
): OrganizationRegistrationRequest {
  const trim = (text: string): string => text.trim();
  const normalizeEmail = (text: string): string => text.trim().toLowerCase();

  return {
    organization: {
      name: trim(value.organization.name),
      legalName: trim(value.organization.legalName),
      taxId: trim(value.organization.taxId),
      email: normalizeEmail(value.organization.email),
      phone: trim(value.organization.phone),
      timezone: trim(value.organization.timezone),
      currency: trim(value.organization.currency).toUpperCase(),
      defaultTaxRate: value.organization.defaultTaxRate ?? 0,
      quotePrefix: trim(value.organization.quotePrefix).toUpperCase(),
      workOrderPrefix: trim(value.organization.workOrderPrefix).toUpperCase(),
      invoicePrefix: trim(value.organization.invoicePrefix).toUpperCase(),
      nextInvoiceNumber: value.organization.nextInvoiceNumber ?? 1,
    },
    branch: {
      name: trim(value.branch.name),
      code: trim(value.branch.code).toUpperCase(),
      phone: trim(value.branch.phone),
      email: normalizeEmail(value.branch.email),
      timezone: trim(value.branch.timezone),
      addressLine1: trim(value.branch.addressLine1),
      city: trim(value.branch.city),
      stateRegion: trim(value.branch.stateRegion),
      postalCode: trim(value.branch.postalCode),
      countryCode: trim(value.branch.countryCode).toUpperCase(),
      businessHours: buildBusinessHoursPayload(value.branch.businessHours),
    },
    owner: {
      firstName: trim(value.owner.firstName),
      lastName: trim(value.owner.lastName),
      email: normalizeEmail(value.owner.email),
      password: value.owner.password,
      phone: trim(value.owner.phone),
    },
  };
}

function buildBusinessHoursPayload(
  days: Record<Weekday, { open: boolean; start: string; end: string }>,
): BusinessHoursPayload {
  const payload: Record<string, BusinessHoursDay> = {};
  for (const day of WEEKDAYS) {
    const value = days[day];
    if (value.open) {
      payload[day] = { start: value.start, end: value.end };
    }
  }
  return payload;
}
