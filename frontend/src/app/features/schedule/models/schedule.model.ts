export type ScheduleView = 'day' | 'week';
export type VisitStatusFilter = 'all' | 'unassigned' | 'assigned' | 'conflicts';
export type UnscheduledFilter = 'all' | 'today' | 'overdue';
export type ConflictCode =
  'missing_skills' | 'time_off' | 'break' | 'outside_availability' | 'overlap';
export type ArrivalWindowCode = 'at_start' | 'start_plus_1h' | 'start_plus_2h' | 'around_1h';
export type LoadState = 'percent' | 'no_availability' | 'none';
export type DispatchPriority = 'urgent' | 'high' | 'normal' | 'low';

export const UNSCHEDULED_PAGE_SIZE = 25;

export interface ScheduleBranch {
  readonly id: string;
  readonly name: string;
  readonly timezone: string;
  readonly isMain: boolean;
}

export interface NamedOption {
  readonly id: string;
  readonly name: string;
}

export interface DispatchOptions {
  readonly branches: readonly ScheduleBranch[];
  readonly defaultBranchId: string | null;
  readonly skills: readonly NamedOption[];
}

export interface Conflict {
  readonly technicianId: string | null;
  readonly code: ConflictCode;
  readonly from: string | null;
  readonly to: string | null;
  readonly label: string;
}

export interface CalendarRange {
  readonly start: string;
  readonly end: string;
}

export interface CalendarDay {
  readonly date: string;
  readonly availability: readonly CalendarRange[];
  readonly breaks: readonly CalendarRange[];
  readonly timeOff: readonly CalendarRange[];
}

export interface CalendarTechnician {
  readonly id: string;
  readonly name: string;
  readonly initials: string;
  readonly colorHex: string | null;
  readonly primarySkill: string | null;
  readonly tag: 'inactive' | 'other_branch' | null;
  readonly load: {
    readonly scheduledMinutes: number;
    readonly availableMinutes: number;
    readonly state: LoadState;
  };
  readonly days: readonly CalendarDay[];
  readonly assessments: readonly CalendarRange[];
}

export interface CalendarVisit {
  readonly visitId: string;
  readonly workOrderId: string;
  readonly displayNumber: string;
  readonly title: string;
  readonly street: string;
  readonly status: string;
  readonly start: string;
  readonly end: string;
  readonly technicianIds: readonly string[];
  readonly primaryTechnicianId: string | null;
  readonly conflicts: readonly Conflict[];
}

export interface CalendarResponse {
  readonly timezone: string;
  readonly from: string;
  readonly to: string;
  readonly days: readonly string[];
  readonly technicians: readonly CalendarTechnician[];
  readonly visits: readonly CalendarVisit[];
}

export interface CalendarQuery {
  readonly branchId: string;
  readonly view: ScheduleView;
  readonly date: string;
  readonly technicianIds: readonly string[];
  readonly skillId: string | null;
  readonly status: VisitStatusFilter;
}

export interface UnscheduledItem {
  readonly visitId: string;
  readonly visitNumber: number;
  readonly recurrenceCount: number | null;
  readonly workOrderId: string;
  readonly displayNumber: string;
  readonly title: string;
  readonly priority: DispatchPriority;
  readonly estimatedDurationMinutes: number | null;
  readonly customerName: string;
  readonly address: string;
  readonly preferredStart: string | null;
  readonly preferredEnd: string | null;
  readonly isOverdue: boolean;
  readonly skills: readonly NamedOption[];
}

export interface UnscheduledResponse {
  readonly items: readonly UnscheduledItem[];
  readonly total: number;
}

export interface UnscheduledQuery {
  readonly branchId: string;
  readonly filter: UnscheduledFilter;
  readonly search: string;
  readonly skillId: string | null;
  readonly page: number;
}

export interface DispatchTechnician {
  readonly id: string;
  readonly name: string;
  readonly initials: string;
  readonly colorHex: string | null;
  readonly primarySkill: string | null;
}

export interface DispatchValues {
  readonly date: string | null;
  readonly start: string | null;
  readonly end: string | null;
  readonly arrivalWindow: ArrivalWindowCode;
  readonly technicianIds: readonly string[];
  readonly primaryTechnicianId: string | null;
  readonly dispatchNote: string | null;
  readonly notifyCustomer: boolean;
  readonly sendTechnicianDetails: boolean;
}

export interface VisitDispatchDetail {
  readonly visitId: string;
  readonly visitNumber: number;
  readonly recurrence: { readonly count: number } | null;
  readonly status: string;
  readonly updatedAt: string;
  readonly canManage: boolean;
  readonly isLocked: boolean;
  readonly timezone: string;
  readonly workOrder: {
    readonly id: string;
    readonly displayNumber: string;
    readonly title: string;
    readonly priority: DispatchPriority;
    readonly estimatedDurationMinutes: number | null;
    readonly requiredSkills: readonly NamedOption[];
    readonly plannedMaterialsCount: number;
  };
  readonly customer: {
    readonly name: string;
    readonly initials: string;
    readonly phone: string | null;
    readonly address: string;
    readonly hasEmail: boolean;
  };
  readonly values: DispatchValues;
  readonly technicians: readonly DispatchTechnician[];
}

export interface EvaluationRequest {
  readonly date: string;
  readonly start: string;
  readonly end: string;
  readonly technicianIds: readonly string[];
  readonly primaryTechnicianId: string | null;
}

export interface Check {
  readonly passed: boolean;
  readonly code: ConflictCode | null;
  readonly label: string;
}

export interface VisitEvaluation {
  readonly conflicts: readonly Conflict[];
  readonly skills: Check;
  readonly checks: readonly {
    readonly technicianId: string;
    readonly availability: Check;
    readonly overlap: Check;
  }[];
  readonly impact: readonly {
    readonly technicianId: string;
    readonly jobs: number;
    readonly scheduledMinutes: number;
    readonly availableMinutes: number;
    readonly previous: string | null;
    readonly next: string | null;
  }[];
  readonly ranking: readonly {
    readonly technicianId: string;
    readonly bestMatch: boolean;
    readonly conflictCount: number;
    readonly loadPercent: number | null;
  }[];
}

export interface DispatchBody extends DispatchValues {
  readonly date: string;
  readonly start: string;
  readonly end: string;
  readonly overrideReason: string | null;
  readonly updatedAt: string;
}

export interface VisitDispatchResult {
  readonly visit: VisitDispatchDetail;
  readonly workOrderStatus: string;
  readonly notified: boolean;
  readonly materializedVisitId: string | null;
}
