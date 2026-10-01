import { ProfileStatus } from './team.model';

export type ExceptionKind = 'extended' | 'partial' | 'unavailable';
export type ExceptionStatus = 'active' | 'cancelled';
export type UpcomingLabel = 'today' | 'tomorrow' | 'date';
export type UpcomingState = 'available_now' | 'next_available' | 'windows' | 'unavailable';

export interface WeeklyDayDto {
  readonly dayOfWeek: number;
  readonly start: string;
  readonly end: string;
  readonly breakStart?: string | null;
  readonly breakEnd?: string | null;
  readonly capacityPercent: number;
}

export interface AssignedSkillDto {
  readonly skillId: string;
  readonly name: string;
  readonly proficiency: number;
  readonly isPrimary: boolean;
}

export interface TechnicianException {
  readonly id: string;
  readonly date: string;
  readonly kind: ExceptionKind;
  readonly start?: string | null;
  readonly end?: string | null;
  readonly reason: string;
  readonly status: ExceptionStatus;
  readonly version: string;
}

export interface UpcomingDay {
  readonly date: string;
  readonly label: UpcomingLabel;
  readonly state: UpcomingState;
  readonly nextAvailableAt?: string | null;
  readonly windows: readonly { readonly start: string; readonly end: string }[];
  readonly limited: boolean;
}

export interface SkillsAvailability {
  readonly technician: {
    readonly id: string;
    readonly firstName: string;
    readonly lastName: string;
    readonly profileStatus: ProfileStatus;
    readonly branch: { readonly id: string; readonly name: string };
  };
  readonly version: string;
  readonly timezone: string;
  readonly timezoneLabel: string;
  readonly weeklyAvailability: readonly WeeklyDayDto[];
  readonly skills: readonly AssignedSkillDto[];
  readonly exceptions: readonly TechnicianException[];
  readonly upcoming: readonly UpcomingDay[];
  readonly capacity: {
    readonly availableMinutes: number;
    readonly scheduledMinutes: number;
    readonly remainingMinutes: number;
    readonly utilizationPercent?: number | null;
  };
  readonly today: { readonly jobs: number; readonly bookedPercent?: number | null };
}

export interface WeeklyDayRequest {
  readonly dayOfWeek: number;
  readonly start: string;
  readonly end: string;
  readonly breakStart?: string;
  readonly breakEnd?: string;
}

export interface SkillAssignmentRequest {
  readonly skillId: string;
  readonly proficiency: number;
  readonly isPrimary: boolean;
}

export interface SkillsAvailabilityRequest {
  readonly version: string;
  readonly weeklyAvailability: readonly WeeklyDayRequest[];
  readonly skills: readonly SkillAssignmentRequest[];
}

export interface ExceptionRequest {
  readonly date: string;
  readonly kind: ExceptionKind;
  readonly start?: string;
  readonly end?: string;
  readonly reason: string;
}

export interface CatalogSkill {
  readonly id: string;
  readonly name: string;
  readonly description?: string | null;
  readonly isActive: boolean;
}

export interface CatalogSkillRequest {
  readonly name: string;
  readonly description?: string;
}

/** Editable weekly row (Monday-first in the table). Times are `HH:mm`. */
export interface DayForm {
  readonly dayOfWeek: number;
  readonly on: boolean;
  readonly start: string;
  readonly end: string;
  readonly breakStart: string | null;
  readonly breakEnd: string | null;
  /** Stored value kept for days On at load; 100 otherwise (BR-07). Never copied (BR-08). */
  readonly capacityPercent: number;
}

export interface SkillRow {
  readonly skillId: string;
  readonly name: string;
  readonly proficiency: number;
  readonly isPrimary: boolean;
}
