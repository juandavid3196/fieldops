import { FormControl, FormGroup } from '@angular/forms';

import {
  BusinessHoursControls,
  BusinessHoursPayload,
  Weekday,
  buildBusinessHoursPayload,
} from './organization-registration.model';

import type { SimpleFieldKey as RegistrationFieldKey } from './organization-registration.model';

// --- Organization settings (FR-03/FR-04, BR-01/BR-02) ----------------------

/** Flat body of `GET`/`PUT /organization-settings` (BR-01 fields + `updatedAt`). */
export interface OrganizationSettingsResponse {
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
  readonly updatedAt: string;
  /** `true` only for the CompanySettingsManage policy (Owner). */
  readonly canManage: boolean;
}

/** Body of `PUT /organization-settings`: BR-01 fields plus the loaded `updatedAt` (BR-07). */
export interface OrganizationSettingsRequest {
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
  readonly updatedAt: string;
}

/**
 * Field keys shown by the three reused onboarding sections, scoped to this
 * page's organization fields (the sections hardcode this exact `organization.*`
 * namespace from `organization-registration.model`).
 */
export type OrganizationFieldKey = Extract<RegistrationFieldKey, `organization.${string}`>;

/** Displayed organization field messages; a field is absent when valid. */
export type OrganizationFieldErrors = Partial<Record<OrganizationFieldKey, string>>;

// --- Branches (FR-05 to FR-09, BR-03 to BR-08) ------------------------------

/** One row of `GET /branches`. */
export interface BranchListItem {
  readonly id: string;
  readonly name: string;
  readonly code: string;
  readonly addressLine1: string;
  readonly addressLine2: string;
  readonly city: string;
  readonly stateRegion: string;
  readonly postalCode: string;
  readonly countryCode: string;
  readonly timezone: string;
  readonly isActive: boolean;
}

export interface BranchListResponse {
  readonly items: readonly BranchListItem[];
}

/** Body of `GET /branches/{id}`, and the `201`/`200` response of create/update. */
export interface BranchDetail extends BranchListItem {
  readonly email: string;
  readonly phone: string;
  readonly businessHours: BusinessHoursPayload;
  readonly updatedAt: string;
}

/** Body of `POST /branches` (BR-03 fields). */
export interface BranchRequest {
  readonly name: string;
  readonly code: string;
  readonly phone: string;
  readonly email: string;
  readonly timezone: string;
  readonly addressLine1: string;
  readonly addressLine2: string;
  readonly city: string;
  readonly stateRegion: string;
  readonly postalCode: string;
  readonly countryCode: string;
  readonly businessHours: BusinessHoursPayload;
}

/** Body of `PUT /branches/{id}`: BR-03 fields plus the loaded `updatedAt` (BR-07). */
export interface BranchUpdateRequest extends BranchRequest {
  readonly updatedAt: string;
}

// --- Branch drawer form (browser-only; never sent as-is) -------------------

export interface BranchDrawerFormControls {
  name: FormControl<string>;
  code: FormControl<string>;
  phone: FormControl<string>;
  email: FormControl<string>;
  timezone: FormControl<string>;
  addressLine1: FormControl<string>;
  addressLine2: FormControl<string>;
  city: FormControl<string>;
  stateRegion: FormControl<string>;
  postalCode: FormControl<string>;
  countryCode: FormControl<string>;
  businessHours: FormGroup<BusinessHoursControls>;
}

/** Raw value shape of `BranchDrawerFormControls` (`form.getRawValue()`). */
export interface BranchDrawerFormValue {
  name: string;
  code: string;
  phone: string;
  email: string;
  timezone: string;
  addressLine1: string;
  addressLine2: string;
  city: string;
  stateRegion: string;
  postalCode: string;
  countryCode: string;
  businessHours: Record<Weekday, { open: boolean; start: string; end: string }>;
}

/**
 * Every simple (non business-hours) branch field key. Kept in the same
 * `branch.*` namespace as `organization-registration.model` so the reused
 * `business-hours-editor` (whose key-builder functions hardcode that prefix)
 * and the `FIELD_LABELS`-style lookup pattern stay consistent.
 */
export type BranchSimpleFieldKey =
  | 'branch.name'
  | 'branch.code'
  | 'branch.phone'
  | 'branch.email'
  | 'branch.timezone'
  | 'branch.addressLine1'
  | 'branch.addressLine2'
  | 'branch.city'
  | 'branch.stateRegion'
  | 'branch.postalCode'
  | 'branch.countryCode';

export type BranchBusinessHoursFieldKey = `branch.businessHours.${Weekday}.${'start' | 'end'}`;
export type BranchBusinessHoursRootKey = 'branch.businessHours';

export type BranchFieldKey =
  BranchSimpleFieldKey | BranchBusinessHoursFieldKey | BranchBusinessHoursRootKey;

/** Displayed branch field messages; a field is absent when valid. */
export type BranchFieldErrors = Partial<Record<BranchFieldKey, string>>;

/** Builds the BR-03 request body from the raw drawer form value. */
export function buildBranchRequest(value: BranchDrawerFormValue): BranchRequest {
  const trim = (text: string): string => text.trim();
  const normalizeEmail = (text: string): string => text.trim().toLowerCase();

  return {
    name: trim(value.name),
    code: trim(value.code).toUpperCase(),
    phone: trim(value.phone),
    email: normalizeEmail(value.email),
    timezone: trim(value.timezone),
    addressLine1: trim(value.addressLine1),
    addressLine2: trim(value.addressLine2),
    city: trim(value.city),
    stateRegion: trim(value.stateRegion),
    postalCode: trim(value.postalCode),
    countryCode: trim(value.countryCode).toUpperCase(),
    businessHours: buildBusinessHoursPayload(value.businessHours),
  };
}
