export type BusinessHoursDayName =
  'monday' | 'tuesday' | 'wednesday' | 'thursday' | 'friday' | 'saturday' | 'sunday';

/** Monday-Sunday, the order of the API `days` array and of the editor rows. */
export const BUSINESS_HOURS_DAYS: readonly BusinessHoursDayName[] = [
  'monday',
  'tuesday',
  'wednesday',
  'thursday',
  'friday',
  'saturday',
  'sunday',
];

export const BUSINESS_HOURS_DAY_LABELS: Readonly<Record<BusinessHoursDayName, string>> = {
  monday: 'Monday',
  tuesday: 'Tuesday',
  wednesday: 'Wednesday',
  thursday: 'Thursday',
  friday: 'Friday',
  saturday: 'Saturday',
  sunday: 'Sunday',
};

/** `GET`/`PUT /branches/{id}/business-hours` day; closed days carry `null` times. */
export interface BusinessHoursDay {
  readonly dayOfWeek: BusinessHoursDayName;
  readonly isOpen: boolean;
  readonly openingTime: string | null;
  readonly closingTime: string | null;
}

export interface BusinessHoursBody {
  readonly branchId: string;
  readonly branchName: string;
  readonly timezone: string | null;
  readonly updatedAt: string;
  readonly canManage: boolean;
  readonly days: readonly BusinessHoursDay[];
}

export interface BusinessHoursRequest {
  readonly updatedAt: string;
  readonly days: readonly BusinessHoursDay[];
}

/** Editable row state. Closed rows keep placeholder times that are never sent. */
export interface BusinessHoursRow {
  readonly day: BusinessHoursDayName;
  readonly isOpen: boolean;
  readonly opening: string | null;
  readonly closing: string | null;
}

export type BusinessHoursErrors = Readonly<Record<string, string>>;
