import { ApiActiveVisit } from '../../../core/models/api-error.model';

export type TodayVisitStatus =
  | 'scheduled'
  | 'assigned'
  | 'on_the_way'
  | 'in_progress'
  | 'paused'
  | 'needs_correction'
  | 'completed'
  | 'approved'
  | 'cancelled';

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
  /** The caller holds the primary assignment; only the primary technician manages travel. */
  readonly isPrimary: boolean;
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
  /** Past active visits still open (BR-17); treated as empty when absent. */
  readonly unresolvedVisits?: readonly UnresolvedVisit[];
}

/** Active or unresolved visit shown in the blocking card and the unresolved banner. */
export type ActiveVisitSummary = ApiActiveVisit;

/** Past active visit with the context the Unfinished jobs sheet shows (BR-08). */
export interface UnresolvedVisit extends ActiveVisitSummary {
  readonly customerName: string;
  readonly customerType: 'person' | 'company';
  /** ISO instant the visit entered its current status, or `null` when unknown. */
  readonly statusSince: string | null;
}

export interface ScheduleVisit {
  readonly visitId: string;
  /** Work order display number, for example `WO-1042`. */
  readonly displayNumber: string;
  readonly title: string;
  readonly status: TodayVisitStatus;
  /** ISO instants with offset. */
  readonly start: string;
  readonly end: string;
  readonly durationMinutes: number;
  readonly customerName: string;
  readonly customerType: 'person' | 'company';
  readonly address: {
    readonly line1: string;
    readonly city: string;
    readonly stateRegion: string | null;
    readonly postalCode: string | null;
  };
  readonly needsReschedule: boolean;
}

export interface ScheduleDay {
  /** `YYYY-MM-DD` in the response time zone. */
  readonly date: string;
  readonly visitCount: number;
  readonly scheduledMinutes: number;
}

export interface ScheduleResponse {
  readonly timezone: string;
  readonly today: string;
  readonly weekStart: string;
  readonly days: readonly ScheduleDay[];
  readonly visits: readonly ScheduleVisit[];
  readonly unresolvedVisits: readonly UnresolvedVisit[];
}

export interface VisitCancellation {
  readonly reason: string;
  /** ISO instant with offset. */
  readonly cancelledAt: string;
}

export type CustomerType = 'person' | 'company';
export type ContactPreference = 'email' | 'sms' | 'email_or_sms';
export type MaterialSource = 'truck_stock' | 'warehouse' | 'to_purchase';

export interface VisitAccess {
  readonly instructions: string | null;
  readonly contactPreference: ContactPreference | null;
  readonly activeDamage: boolean;
}

export interface VisitTravel {
  /** ISO instants with offset; `durationMinutes` is set once arrival is recorded. */
  readonly startedAt: string | null;
  readonly arrivedAt: string | null;
  readonly durationMinutes: number | null;
}

export interface PlannedMaterial {
  readonly id: string;
  readonly description: string;
  readonly quantity: number;
  readonly unit: string;
  readonly source: MaterialSource;
  /** Quantity used so far; 0 without a recorded row. */
  readonly usedQuantity: number;
}

/** Material added by the technician: an active catalog product or free text. */
export interface AdditionalMaterial {
  readonly id: string;
  readonly description: string;
  readonly quantity: number;
  readonly unit: string;
  readonly catalogItemId: string | null;
}

export interface VisitTask {
  readonly id: string;
  readonly label: string;
  readonly isRequired: boolean;
  readonly isCompleted: boolean;
  readonly notes: string | null;
  /** ISO instant with offset. */
  readonly completedAt: string | null;
}

export interface VisitAssessment {
  readonly completedAt: string;
  readonly diagnosis: string | null;
  readonly recommendedScope: string | null;
  readonly photos: readonly { readonly id: string }[];
}

export type EvidenceType = 'before' | 'after';

export interface VisitEvidence {
  readonly id: string;
  readonly type: EvidenceType;
  readonly createdAt: string;
}

export interface VisitActiveEntry {
  readonly type: 'work' | 'pause';
  /** ISO instant with offset. */
  readonly startedAt: string;
}

/** Closed entry totals in whole seconds; the active entry is added by the client clock. */
export interface VisitTime {
  readonly workSeconds: number;
  readonly pauseSeconds: number;
  readonly activeEntry: VisitActiveEntry | null;
  readonly estimatedMinutes: number | null;
}

export interface CatalogItem {
  readonly id: string;
  readonly name: string;
  readonly unit: string;
}

/** BR-11: server-side completion readiness of the visit. */
export interface VisitCompletion {
  readonly requiredTasksComplete: boolean;
  readonly hasBeforePhoto: boolean;
  readonly hasAfterPhoto: boolean;
  readonly ready: boolean;
  /** Completing this visit now would also complete the work order. */
  readonly completesWorkOrder: boolean;
}

export interface TechnicianVisitDetail extends TodayVisit {
  readonly date: string;
  readonly timezone: string;
  readonly dispatchNote: string | null;
  readonly customerType: CustomerType;
  readonly access: VisitAccess;
  readonly instructions: string | null;
  readonly scope: string;
  readonly requiredSkills: readonly string[];
  readonly officePhone: string | null;
  readonly travel: VisitTravel;
  readonly plannedMaterials: readonly PlannedMaterial[];
  readonly tasks: readonly VisitTask[];
  readonly assessment: VisitAssessment | null;
  readonly actualStartedAt: string | null;
  readonly time: VisitTime;
  readonly additionalMaterials: readonly AdditionalMaterial[];
  readonly evidence: readonly VisitEvidence[];
  readonly technicianNotes: string | null;
  /** First and last name of the active primary assignee. */
  readonly primaryTechnicianName: string;
  readonly completion: VisitCompletion;
  /** Latest cancellation of a `cancelled` visit; `null`/absent otherwise. */
  readonly cancellation?: VisitCancellation | null;
  /** Past `scheduled`/`assigned` visit: dispatch must reschedule it (BR-06). */
  readonly needsReschedule?: boolean;
}

/** Response of start-travel, arrive, start-job, pause and resume; `changed` is false on an idempotent repeat. */
export interface TravelResult {
  readonly changed: boolean;
  readonly visit: TechnicianVisitDetail;
}

export type EvidenceUploadEvent =
  | { readonly kind: 'progress'; readonly percent: number }
  | { readonly kind: 'done'; readonly visit: TechnicianVisitDetail };

export interface TaskUpdate {
  readonly isCompleted?: boolean;
  readonly notes?: string;
}

export type NewMaterial =
  | { readonly quantity: number; readonly catalogItemId: string }
  | { readonly quantity: number; readonly description: string; readonly unit: string };
