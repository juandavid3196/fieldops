export type TodayVisitStatus =
  | 'scheduled'
  | 'assigned'
  | 'on_the_way'
  | 'in_progress'
  | 'paused'
  | 'needs_correction'
  | 'completed'
  | 'approved';

export interface VisitAddress {
  readonly line1: string;
  readonly line2: string | null;
  readonly city: string;
  readonly stateRegion: string | null;
  readonly postalCode: string | null;
  readonly countryCode: string;
}

export interface TodayVisit {
  readonly visitId: string;
  readonly visitNumber: number;
  readonly workOrderId: string;
  /** Work order display number, for example `WO-1042`. */
  readonly displayNumber: string;
  readonly title: string;
  readonly status: TodayVisitStatus;
  /** ISO instants with offset. */
  readonly start: string;
  readonly end: string;
  readonly arrivalWindowStart: string | null;
  readonly arrivalWindowEnd: string | null;
  /** Priority 1 Urgent, 2 High; the code form is accepted too. */
  readonly priority: number | string;
  readonly serviceCategory: string;
  readonly customerName: string;
  readonly phone: string | null;
  readonly address: VisitAddress;
  readonly latitude: number | null;
  readonly longitude: number | null;
  readonly plannedMaterialsCount: number;
}

export interface TodayTechnician {
  readonly firstName: string;
  readonly initials: string;
  readonly colorHex: string | null;
}

export interface TodayMetrics {
  readonly jobs: number;
  readonly scheduledMinutes: number;
  readonly completed: number;
  readonly remaining: number;
}

export interface TodayResponse {
  /** `YYYY-MM-DD` in `timezone`. */
  readonly date: string;
  readonly timezone: string;
  readonly technician: TodayTechnician;
  readonly metrics: TodayMetrics;
  readonly nextVisitId: string | null;
  readonly visits: readonly TodayVisit[];
}

export interface TechnicianVisitDetail extends TodayVisit {
  readonly date: string;
  readonly timezone: string;
  readonly dispatchNote: string | null;
}
