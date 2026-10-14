export type DashboardPeriod = '7' | '30';
export type StatusGroup = 'scheduled' | 'active' | 'completed';
export type AttentionType = 'delayed' | 'unassigned' | 'over_estimate' | 'over_capacity';
export type TechnicianStatus = 'time_off' | 'off' | 'break' | 'on_job' | 'available';
export type ActivityType = 'on_the_way' | 'started' | 'completed' | 'assigned' | 'reassigned';

export interface DashboardBranch {
  readonly id: string;
  readonly name: string;
  readonly timezone: string | null;
}

export interface DashboardTechnicianOption {
  readonly id: string;
  readonly fullName: string;
  readonly branchId: string;
}

export interface NamedOption {
  readonly id: string;
  readonly name: string;
}

export interface DashboardOptions {
  readonly branches: readonly DashboardBranch[];
  readonly organizationTimezone: string;
  readonly technicians: readonly DashboardTechnicianOption[];
  readonly categories: readonly NamedOption[];
}

/** URL state: `branchId` null means All branches. */
export interface DashboardRoute {
  readonly branchId: string | null;
  readonly date: string;
  readonly technicianId: string | null;
  readonly categoryId: string | null;
  readonly period: DashboardPeriod;
}

export interface DashboardQuery {
  readonly branchId: string | null;
  readonly date: string;
  readonly technicianId: string | null;
  readonly categoryId: string | null;
}

export interface PerformanceQuery extends DashboardQuery {
  readonly period: DashboardPeriod;
}

export interface VisitTechnician {
  readonly id: string;
  readonly fullName: string;
}

export interface DayVisit {
  readonly visitId: string;
  readonly workOrderId: string;
  readonly displayNumber: string;
  readonly title: string;
  readonly customerName: string;
  readonly customerPhone: string | null;
  readonly branchId: string;
  readonly status: string;
  readonly statusGroup: StatusGroup;
  readonly isDelayed: boolean;
  readonly delayMinutes: number | null;
  readonly isAtRisk: boolean;
  readonly scheduledStart: string;
  readonly scheduledEnd: string;
  readonly actualCompletedAt: string | null;
  readonly projectedEnd: string | null;
  readonly technician: VisitTechnician | null;
  readonly otherTechnicianCount: number;
}

export interface AttentionTechnician {
  readonly id: string;
  readonly fullName: string;
  readonly scheduledMinutes: number;
  readonly availableMinutes: number;
  readonly percent: number;
}

export interface Attention {
  readonly type: AttentionType;
  readonly visitId: string | null;
  readonly workOrderId: string | null;
  readonly displayNumber: string | null;
  readonly title: string | null;
  readonly visitStatus: string | null;
  readonly branchId: string;
  readonly at: string | null;
  readonly minutes: number | null;
  readonly technician: AttentionTechnician | null;
}

export interface TechnicianAvailability {
  readonly id: string;
  readonly fullName: string;
  readonly initials: string;
  readonly colorHex: string | null;
  readonly skills: readonly string[];
  readonly extraSkillCount: number;
  readonly currentStatus: TechnicianStatus | null;
  readonly scheduledMinutes: number;
  readonly availableMinutes: number;
  readonly percent: number | null;
  readonly nextAvailable: string | null;
}

export interface DurationCategory {
  readonly categoryId: string;
  readonly name: string;
  readonly visitCount: number;
  readonly estimatedMinutes: number;
  readonly actualMinutes: number;
}

export interface ActivityEvent {
  readonly type: ActivityType;
  readonly at: string;
  readonly visitId: string;
  readonly workOrderId: string;
  readonly displayNumber: string;
  readonly title: string;
  readonly technicianName: string | null;
}

export interface DashboardKpis {
  readonly jobs: {
    readonly total: number;
    readonly completed: number;
    readonly active: number;
    readonly scheduled: number;
  };
  readonly unassigned: number;
  readonly atRiskOrDelayed: number;
  readonly utilization: {
    readonly scheduledMinutes: number;
    readonly availableMinutes: number;
    readonly percent: number | null;
  };
  readonly onTimeArrival: {
    readonly percent: number | null;
    readonly eligible: number;
    readonly previousPercent: number | null;
  };
}

export interface DashboardResponse {
  readonly timezone: string;
  readonly date: string;
  readonly reference: string;
  readonly kpis: DashboardKpis;
  readonly visits: readonly DayVisit[];
  readonly attention: readonly Attention[];
  readonly technicians: readonly TechnicianAvailability[];
  readonly durations: {
    readonly visitCount: number;
    readonly categories: readonly DurationCategory[];
  };
  readonly activity: readonly ActivityEvent[];
}

export interface PerformanceResponse {
  readonly from: string;
  readonly to: string;
  readonly jobsCompleted: { readonly completed: number; readonly total: number };
  readonly firstTimeFix: { readonly fixed: number; readonly total: number };
  readonly rating: { readonly average: number | null; readonly count: number };
  readonly daily: readonly { readonly date: string; readonly completed: number }[];
}
