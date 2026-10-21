import { TeamOption } from './team.model';

export type TeamTab = 'members' | 'availability' | 'skills';
export type AvailabilityFilter = 'all' | 'available' | 'time_off' | 'none';
export type SkillCoverageFilter = 'all' | 'with' | 'without';
export type DayState = 'available' | 'time_off' | 'unavailable';

export type DayOfWeekName =
  'monday' | 'tuesday' | 'wednesday' | 'thursday' | 'friday' | 'saturday' | 'sunday';

export interface AvailabilityQuery {
  /** `null` asks for the current week (organization time zone, resolved server-side). */
  readonly weekStart: string | null;
  readonly search: string;
  readonly branchId: string | null;
  readonly availability: AvailabilityFilter;
  readonly page: number;
}

export interface GridDay {
  readonly date: string;
  readonly state: DayState;
  readonly start: string | null;
  readonly end: string | null;
}

export interface AvailabilityRow {
  readonly technicianId: string;
  readonly firstName: string;
  readonly lastName: string;
  readonly colorHex: string | null;
  readonly branch: TeamOption;
  readonly timezone: string;
  readonly usesBranchHours: boolean;
  readonly days: readonly GridDay[];
  readonly weeklyMinutes: number;
}

export interface AvailabilityPage {
  readonly weekStart: string;
  readonly items: readonly AvailabilityRow[];
  readonly totalCount: number;
  readonly page: number;
  readonly pageSize: number;
}

export interface AvailabilityDay {
  readonly dayOfWeek: DayOfWeekName;
  readonly isAvailable: boolean;
  readonly start: string | null;
  readonly end: string | null;
}

export interface TimeOff {
  readonly id: string;
  readonly startDate: string;
  readonly endDate: string;
  readonly note: string | null;
  readonly updatedAt: string;
}

export interface TechnicianAvailability {
  readonly technicianId: string;
  readonly firstName: string;
  readonly lastName: string;
  readonly branch: TeamOption;
  readonly timezone: string;
  readonly timezoneLabel: string;
  readonly usesBranchHours: boolean;
  readonly updatedAt: string;
  readonly days: readonly AvailabilityDay[];
  readonly timeOff: readonly TimeOff[];
}

export interface TimeOffRequest {
  readonly startDate: string;
  readonly endDate: string;
  readonly note?: string;
}

export interface SkillsMatrixQuery {
  readonly search: string;
  readonly branchId: string | null;
  readonly skillId: string | null;
  readonly coverage: SkillCoverageFilter;
  readonly sort: 'asc' | 'desc';
  readonly page: number;
}

export interface MatrixRow {
  readonly technicianId: string;
  readonly firstName: string;
  readonly lastName: string;
  readonly colorHex: string | null;
  readonly branch: TeamOption;
  readonly skillIds: readonly string[];
  readonly skillCount: number;
}

export interface SkillsMatrixPage {
  readonly skills: readonly TeamOption[];
  readonly items: readonly MatrixRow[];
  readonly totalCount: number;
  readonly page: number;
  readonly pageSize: number;
}

export interface SkillAssignments {
  readonly technicianId: string;
  readonly firstName: string;
  readonly lastName: string;
  readonly updatedAt: string;
  readonly skillIds: readonly string[];
}
