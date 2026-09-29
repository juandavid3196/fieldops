import { FormControl, FormGroup } from '@angular/forms';

import {
  BusinessHoursControls,
  BusinessHoursPayload,
  OrganizationFormControls,
  Weekday,
  buildBusinessHoursPayload,
} from './organization-registration.model';

import type { SimpleFieldKey as RegistrationFieldKey } from './organization-registration.model';

// --- Organization settings (FR-03/FR-04, BR-01/BR-02) ----------------------

/** `logo` of `GET /organization-settings`; the bytes come from `GET /organization-settings/logo`. */
export interface OrganizationLogoMetadata {
  readonly contentType: string;
  readonly sizeBytes: number;
  readonly updatedAt: string;
}

/** Flat body of `GET`/`PUT /organization-settings` (base fields, BR-04 fields, `hasInvoices`, `logo`). */
export interface OrganizationSettingsResponse {
  readonly name: string;
  /** The `organizations` columns below are nullable in the schema and the API returns `null` for them. */
  readonly legalName: string | null;
  readonly taxId: string | null;
  readonly email: string | null;
  readonly phone: string | null;
  readonly timezone: string;
  readonly currency: string;
  readonly defaultTaxRate: number;
  readonly quotePrefix: string;
  readonly workOrderPrefix: string;
  readonly invoicePrefix: string;
  readonly nextInvoiceNumber: number;
  /** Nullable until the Owner completes the new fields on the next save. */
  readonly website: string | null;
  readonly addressLine1: string | null;
  readonly city: string | null;
  readonly stateRegion: string | null;
  readonly postalCode: string | null;
  readonly countryCode: string | null;
  readonly pricesIncludeTax: boolean;
  readonly nextQuoteNumber: number;
  readonly nextWorkOrderNumber: number;
  readonly hasInvoices: boolean;
  readonly logo: OrganizationLogoMetadata | null;
  readonly updatedAt: string;
  /** `true` only for the CompanySettingsManage policy (Owner). */
  readonly canManage: boolean;
}

/** Body of `PUT /organization-settings`: settings fields plus the loaded `updatedAt` (BR-07). */
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
  readonly website: string;
  readonly addressLine1: string;
  readonly city: string;
  readonly stateRegion: string;
  readonly postalCode: string;
  readonly countryCode: string;
  readonly pricesIncludeTax: boolean;
  readonly nextQuoteNumber: number;
  readonly nextWorkOrderNumber: number;
  /** BR-06: sent as `true` only after the Owner confirmed a currency change. */
  readonly confirmCurrencyChange?: boolean;
  readonly updatedAt: string;
}

/** Reactive form shape of the Company setup page (settings fields; browser-only). */
export interface CompanySetupFormControls extends OrganizationFormControls {
  website: FormControl<string>;
  addressLine1: FormControl<string>;
  city: FormControl<string>;
  stateRegion: FormControl<string>;
  postalCode: FormControl<string>;
  countryCode: FormControl<string>;
  pricesIncludeTax: FormControl<boolean>;
  nextQuoteNumber: FormControl<number | null>;
  nextWorkOrderNumber: FormControl<number | null>;
}

/** Every organization field key of Company setup (`organization.*` namespace, BR-04 included). */
export type OrganizationFieldKey =
  | Extract<RegistrationFieldKey, `organization.${string}`>
  | 'organization.website'
  | 'organization.addressLine1'
  | 'organization.city'
  | 'organization.stateRegion'
  | 'organization.postalCode'
  | 'organization.countryCode'
  | 'organization.nextQuoteNumber'
  | 'organization.nextWorkOrderNumber';

/** Displayed organization field messages; a field is absent when valid. */
export type OrganizationFieldErrors = Partial<Record<OrganizationFieldKey, string>>;

// --- Branches (FR-05 to FR-09, BR-03 to BR-08) ------------------------------

/** One row of `GET /branches`. */
export interface BranchListItem {
  readonly id: string;
  readonly name: string;
  readonly code: string;
  readonly addressLine1: string;
  /** Nullable in the schema; the API returns `null` when unset. */
  readonly addressLine2: string | null;
  readonly city: string;
  readonly stateRegion: string | null;
  readonly postalCode: string;
  readonly countryCode: string;
  readonly timezone: string;
  readonly isActive: boolean;
  /** BR-11: exactly one main branch per organization. */
  readonly isMain: boolean;
  /** BR-10: active technicians assigned to the branch. */
  readonly technicianCount: number;
}

export interface BranchListResponse {
  readonly items: readonly BranchListItem[];
}

/** Body of `GET /branches/{id}`, and the `201`/`200` response of create/update. */
export interface BranchDetail extends BranchListItem {
  readonly email: string | null;
  readonly phone: string | null;
  readonly businessHours: BusinessHoursPayload;
  readonly servicePostalCodes: readonly string[];
  readonly usesCompanyBilling: boolean;
  readonly updatedAt: string;
}

/** Body of `POST /branches` (BR-03 fields; BR-09 fields optional on create). */
export interface BranchRequest {
  /** Optional for the API on create; the UI always sends both. */
  readonly servicePostalCodes: readonly string[];
  readonly usesCompanyBilling: boolean;
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
  /** Comma-separated text; converted with {@link parseServicePostalCodes}. */
  servicePostalCodes: FormControl<string>;
  usesCompanyBilling: FormControl<boolean>;
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
  servicePostalCodes: string;
  usesCompanyBilling: boolean;
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
  | 'branch.countryCode'
  | 'branch.servicePostalCodes';

export type BranchBusinessHoursFieldKey = `branch.businessHours.${Weekday}.${'start' | 'end'}`;
export type BranchBusinessHoursRootKey = 'branch.businessHours';

export type BranchFieldKey =
  BranchSimpleFieldKey | BranchBusinessHoursFieldKey | BranchBusinessHoursRootKey;

/** Displayed branch field messages; a field is absent when valid. */
export type BranchFieldErrors = Partial<Record<BranchFieldKey, string>>;

/** BR-09: comma-separated text to a trimmed, de-duplicated list (first occurrence order kept). */
export function parseServicePostalCodes(text: string): string[] {
  const codes = text
    .split(',')
    .map((code) => code.trim())
    .filter((code) => code !== '');
  return [...new Set(codes)];
}

/** Array to the comma-separated text shown in the drawer. */
export function formatServicePostalCodes(codes: readonly string[]): string {
  return codes.join(', ');
}

/** Builds the BR-03/BR-09 request body from the raw drawer form value. */
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
    servicePostalCodes: parseServicePostalCodes(value.servicePostalCodes),
    usesCompanyBilling: value.usesCompanyBilling,
    businessHours: buildBusinessHoursPayload(value.businessHours),
  };
}
