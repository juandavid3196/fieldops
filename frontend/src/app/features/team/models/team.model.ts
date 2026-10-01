export const TEAM_PAGE_SIZE = 10;

export type TeamPeriod = 'today' | 'week';
export type TeamSort = 'name_asc' | 'name_desc';
export type StatusFilter =
  'all_active' | 'available' | 'on_job' | 'break' | 'time_off' | 'off' | 'inactive' | 'suspended';
export type AccountLinkFilter = 'all' | 'linked' | 'not_linked';
export type TodayStatus =
  'on_job' | 'available' | 'break' | 'time_off' | 'off' | 'inactive' | 'suspended';
export type ProfileStatus = 'active' | 'inactive' | 'suspended';
export type WorkloadState = 'percent' | 'no_availability' | 'none';
export type SkillHealth = 'healthy' | 'watch' | 'low';

export interface TeamOption {
  readonly id: string;
  readonly name: string;
}

export interface TeamOptions {
  readonly branches: readonly TeamOption[];
  readonly skills: readonly TeamOption[];
}

export interface CapacityAlert {
  readonly technicianId: string;
  readonly name: string;
  readonly workloadPercent?: number | null;
  readonly noAvailability: boolean;
  readonly moreCount: number;
}

export interface UnlinkedAlert {
  readonly technicianId: string;
  readonly name: string;
  readonly moreCount: number;
}

export interface TeamMetrics {
  readonly activeProfiles: number;
  readonly availableNow: number;
  readonly onJobs: number;
  readonly atCapacity: number;
  readonly unlinkedAccounts: number;
  readonly capacityAlert: CapacityAlert | null;
  readonly unlinkedAlert: UnlinkedAlert | null;
}

export interface NextAvailable {
  readonly kind: 'now' | 'time' | 'none';
  readonly at?: string | null;
}

export interface TechnicianRow {
  readonly id: string;
  readonly fullName: string;
  readonly branchName: string;
  readonly skills: readonly string[];
  readonly isLinked: boolean;
  readonly profileStatus: ProfileStatus;
  readonly todayStatus: TodayStatus;
  readonly jobs: number;
  readonly workloadPercent?: number | null;
  readonly workloadState: WorkloadState;
  readonly atCapacity: boolean;
  readonly nextAvailable: NextAvailable;
  readonly timezone: string;
}

export interface TechnicianListQuery {
  readonly search: string;
  readonly branchId: string | null;
  readonly skillId: string | null;
  readonly status: StatusFilter;
  readonly accountLink: AccountLinkFilter;
  readonly sort: TeamSort;
  readonly period: TeamPeriod;
  readonly page: number;
}

export interface TechnicianListResponse {
  readonly items: readonly TechnicianRow[];
  readonly totalCount: number;
  readonly teamMembersCount: number;
  readonly page: number;
  readonly pageSize: number;
}

export interface TechnicianSkill {
  readonly name: string;
  readonly proficiency?: number | null;
  readonly isPrimary: boolean;
}

export interface AvailabilityDay {
  readonly dayOfWeek: number;
  readonly windows: readonly { readonly start: string; readonly end: string }[];
}

export interface TechnicianToday {
  readonly todayStatus: TodayStatus;
  readonly currentJob: { readonly label: string } | null;
  readonly jobs: number;
  readonly workloadPercent?: number | null;
  readonly workloadState: WorkloadState;
  readonly nextAvailable: NextAvailable;
}

export interface TechnicianDetail {
  readonly id: string;
  readonly firstName: string;
  readonly lastName: string;
  readonly email?: string | null;
  readonly phone?: string | null;
  readonly employeeCode?: string | null;
  readonly notes?: string | null;
  readonly branch: TeamOption;
  readonly profileStatus: ProfileStatus;
  readonly account: { readonly email: string; readonly roleName: string } | null;
  readonly skills: readonly TechnicianSkill[];
  readonly availability: readonly AvailabilityDay[];
  readonly today: TechnicianToday;
  readonly timezone: string;
}

export interface TechnicianRequest {
  readonly firstName: string;
  readonly lastName: string;
  readonly email: string | null;
  readonly phone: string | null;
  readonly employeeCode: string | null;
  readonly branchId: string;
  readonly notes: string | null;
}

export interface LinkableAccount {
  readonly organizationUserId: string;
  readonly fullName: string;
  readonly email: string;
  readonly roleName: string;
}

export interface SkillCoverage {
  readonly skillId: string;
  readonly name: string;
  readonly technicianCount: number;
  readonly health: SkillHealth;
}

export const TECHNICIAN_FIELD_KEYS = [
  'firstName',
  'lastName',
  'email',
  'phone',
  'employeeCode',
  'branchId',
  'notes',
] as const;
export type TechnicianFieldKey = (typeof TECHNICIAN_FIELD_KEYS)[number];
export type TechnicianFieldErrors = Partial<Record<TechnicianFieldKey, string>>;
