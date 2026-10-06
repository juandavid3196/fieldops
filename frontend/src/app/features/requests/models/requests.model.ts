export type RequestStatus =
  | 'new'
  | 'needs_review'
  | 'assessment_scheduled'
  | 'ready_for_quote'
  | 'quoted'
  | 'converted'
  | 'cancelled';
export type BoardStatus = 'new' | 'needs_review' | 'assessment_scheduled' | 'ready_for_quote';
export type Urgency = 'standard' | 'urgent' | 'emergency';
export type RequestSource = 'public_form' | 'internal';
export type CreatedRange = 'today' | '7d' | '30d';
export type DateKind = 'assessment' | 'preferred' | 'asap' | 'flexible' | 'created';
export type DateMode = 'asap' | 'date' | 'flexible';
export type TimeWindow = 'morning' | 'afternoon' | 'evening' | 'any';

export const PAGE_SIZE = 50;

/** Navigation state key carrying a toast across routes (the Assessment page to the board). */
export const TOAST_STATE_KEY = 'toast';
export interface ToastHandoff {
  readonly severity: 'success' | 'error' | 'warn';
  readonly summary: string;
}

/** Fixed copy of the `409` toast (the backend title is never displayed). */
export const CONFLICT_MESSAGE = 'This request changed. Refresh to see the latest.';
export const SAVE_FAILED_MESSAGE = "We couldn't save this change. Please try again.";
export const UNAVAILABLE_MESSAGE = "This request isn't available.";
export const COMPLETE_FAILED_MESSAGE = "We couldn't complete this assessment. Please try again.";
export const QUOTE_DRAFT_EXISTS_MESSAGE = 'This request has a draft quote. Discard it first.';
export const NO_EMAIL_MESSAGE = 'This customer has no email address.';
export const REQUEST_ID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export const BOARD_STATUSES: readonly BoardStatus[] = [
  'new',
  'needs_review',
  'assessment_scheduled',
  'ready_for_quote',
];

export const STATUS_LABELS: Readonly<Record<RequestStatus, string>> = {
  new: 'New',
  needs_review: 'Needs review',
  assessment_scheduled: 'Assessment scheduled',
  ready_for_quote: 'Ready for quote',
  quoted: 'Quoted',
  converted: 'Converted',
  cancelled: 'Cancelled',
};

/** Column headings (BR-05). */
export const COLUMN_LABELS: Readonly<Record<BoardStatus, string>> = {
  new: 'New requests',
  needs_review: 'Needs review',
  assessment_scheduled: 'Assessment scheduled',
  ready_for_quote: 'Ready for quote',
};

/** BR-10. */
export const URGENCY_LABELS: Readonly<Record<Urgency, string>> = {
  standard: 'Standard',
  urgent: 'Urgent',
  emergency: 'Emergency',
};

export const SOURCE_LABELS: Readonly<Record<RequestSource, string>> = {
  public_form: 'Online form',
  internal: 'Internal',
};

export const TIME_WINDOW_LABELS: Readonly<Record<TimeWindow, string>> = {
  morning: 'Morning (8 AM - 12 PM)',
  afternoon: 'Afternoon (12 PM - 5 PM)',
  evening: 'Evening (5 PM - 8 PM)',
  any: 'Any time (8 AM - 8 PM)',
};

export const MANAGER_ROLES: readonly string[] = ['owner', 'dispatcher'];
export const READ_ROLES: readonly string[] = ['operations_manager', 'accounting', 'viewer'];

/** BR-06 filters; `null` means "All". */
export interface RequestFilters {
  /** `unassigned` or a member id. */
  readonly assignee: string | null;
  readonly categoryId: string | null;
  readonly urgency: Urgency | null;
  readonly source: RequestSource | null;
  readonly created: CreatedRange | null;
  readonly search: string;
}

export const NO_FILTERS: RequestFilters = {
  assignee: null,
  categoryId: null,
  urgency: null,
  source: null,
  created: null,
  search: '',
};

export function hasActiveFilters(filters: RequestFilters): boolean {
  return (
    filters.assignee !== null ||
    filters.categoryId !== null ||
    filters.urgency !== null ||
    filters.source !== null ||
    filters.created !== null ||
    filters.search.trim().length > 0
  );
}

// API (Final contract: specs/requests-pipeline/spec.md)

export interface RequestAvatar {
  readonly role: 'technician' | 'assignee';
  readonly name: string;
  readonly initials: string;
}

export interface RequestCard {
  readonly id: string;
  readonly number: string;
  readonly title: string;
  readonly customerName: string | null;
  readonly categoryName: string | null;
  readonly dateKind: DateKind;
  readonly date: string | null;
  readonly urgency: Urgency;
  readonly avatar: RequestAvatar | null;
  readonly createdAt: string;
  readonly awaitingResponse: boolean;
}

export interface PipelineColumn {
  readonly status: BoardStatus;
  readonly total: number;
  readonly items: readonly RequestCard[];
}

export interface PipelineResponse {
  readonly columns: readonly PipelineColumn[];
  readonly timezone: string;
}

export interface MetricValue {
  readonly value: number;
  readonly deltaPercent: number | null;
}

export interface RequestMetrics {
  readonly newToday: MetricValue;
  readonly awaitingResponse: MetricValue;
  readonly assessmentsToday: MetricValue;
  readonly conversionRate: { readonly value: number; readonly deltaPoints: number | null };
}

export interface OptionCategory {
  readonly id: string;
  readonly name: string;
  readonly services: readonly { readonly id: string; readonly name: string }[];
}

export interface OptionAssignee {
  readonly userId: string;
  readonly name: string;
  readonly initials: string;
  readonly branchIds: readonly string[] | 'all';
}

export interface OptionTechnician {
  readonly id: string;
  readonly name: string;
  readonly initials: string;
  readonly branchId: string;
}

export interface RequestOptions {
  readonly requestPrefix: string;
  readonly timezone: string;
  readonly categories: readonly OptionCategory[];
  readonly assignees: readonly OptionAssignee[];
  readonly branches: readonly { readonly id: string; readonly name: string }[];
  readonly technicians: readonly OptionTechnician[];
}

export interface CustomerOption {
  readonly id: string;
  readonly displayName: string;
  readonly branchId: string | null;
  readonly contacts: readonly {
    readonly id: string;
    readonly name: string;
    readonly email: string | null;
    readonly phone: string | null;
    readonly isPrimary: boolean;
  }[];
  readonly properties: readonly {
    readonly id: string;
    readonly name: string | null;
    readonly addressLine1: string;
    readonly city: string;
    readonly isPrimary: boolean;
  }[];
}

export interface CustomerOptionsResponse {
  readonly items: readonly CustomerOption[];
}

export interface RequestAttachment {
  readonly id: string;
  readonly fileName: string;
  readonly mimeType: string;
  readonly sizeBytes: number;
  readonly createdAt: string;
}

export interface AssessmentPhoto {
  readonly id: string;
  readonly fileName: string;
  readonly mimeType: string;
  readonly sizeBytes: number;
}

/** BR-03 (quote-builder): the latest completed assessment with its findings. */
export interface CompletedAssessment {
  readonly id: string;
  readonly start: string;
  readonly completedAt: string;
  readonly technician: { readonly id: string; readonly name: string } | null;
  readonly diagnosis: string | null;
  readonly recommendedScope: string | null;
  readonly photos: readonly AssessmentPhoto[];
}

/** The request's quote, if any (BR-04). */
export interface RequestQuote {
  readonly id: string;
  readonly status: string;
  readonly hasDraft: boolean;
}

export interface RequestAddress {
  readonly line1?: string | null;
  readonly line2?: string | null;
  readonly city?: string | null;
  readonly state?: string | null;
  readonly postalCode?: string | null;
}

export interface RequestDetail {
  readonly id: string;
  readonly number: string;
  readonly title: string;
  readonly status: RequestStatus;
  readonly urgency: Urgency;
  readonly source: RequestSource;
  readonly createdAt: string;
  readonly branch: { readonly id: string; readonly name: string } | null;
  readonly assignee: {
    readonly userId: string;
    readonly name: string;
    readonly initials?: string;
  } | null;
  readonly customer: { readonly id: string; readonly name: string } | null;
  readonly contact: {
    readonly name: string | null;
    readonly phone: string | null;
    readonly email: string | null;
  };
  /** The contract names the field only; the API returns a structured address (a string is also accepted). */
  readonly serviceAddress: string | RequestAddress | null;
  readonly category: { readonly id: string; readonly name: string } | null;
  readonly service: { readonly id: string; readonly name: string } | null;
  readonly availability: {
    readonly dateMode: DateMode;
    readonly preferredDate: string | null;
    readonly timeWindow: TimeWindow | null;
    readonly schedulingNotes: string | null;
  } | null;
  readonly hasActiveDamage: boolean;
  readonly description: string;
  readonly awaitingResponse: boolean;
  readonly assessment: {
    readonly id: string;
    readonly start: string;
    readonly end: string;
    readonly technician: {
      readonly id: string;
      readonly name: string;
      readonly initials?: string;
    } | null;
    /** BR-17: internal data; never shown to the customer. */
    readonly purpose: string | null;
    readonly internalInstructions: string | null;
  } | null;
  readonly completedAssessment?: CompletedAssessment | null;
  readonly quote?: RequestQuote | null;
  readonly attachments: readonly RequestAttachment[];
  readonly notes: readonly {
    readonly id: string;
    readonly body: string;
    readonly authorName: string | null;
    readonly createdAt: string;
  }[];
  readonly activity: readonly {
    readonly kind: string;
    readonly label: string;
    readonly detail: string | null;
    readonly actorName: string | null;
    readonly occurredAt: string;
  }[];
}

export interface CreateRequestBody {
  readonly customerId: string;
  readonly contactId: string;
  readonly propertyId: string;
  readonly categoryId: string;
  readonly serviceId: string | null;
  readonly notSure: boolean;
  readonly description: string;
  readonly urgency: Urgency;
  readonly hasActiveDamage: boolean;
  readonly availability: {
    readonly dateMode: DateMode;
    readonly preferredDate: string | null;
    readonly timeWindow: TimeWindow | null;
    readonly schedulingNotes: string | null;
  };
}

/** Schedule/reschedule body (BR-20); `start`/`end` are org-local `YYYY-MM-DDTHH:mm`. */
export interface AssessmentBody {
  readonly start: string;
  readonly end: string;
  readonly technicianId: string;
  /** Only for a request with no branch. */
  readonly branchId?: string;
  readonly purpose: string;
  readonly internalInstructions: string | null;
  readonly notifyCustomer: boolean;
}

/** Complete assessment dialog value (BR-01). */
export interface CompleteAssessmentBody {
  readonly diagnosis: string;
  readonly recommendedScope: string | null;
  readonly photos: readonly File[];
}

// Assessment planner and calendar (Final contract: specs/schedule-assessment/spec.md)

export type SlotState =
  'available' | 'available_after' | 'outside_availability' | 'conflict' | 'time_off';

export interface TechnicianSlot {
  readonly state: SlotState;
  readonly from: string | null;
  readonly to: string | null;
  readonly availableAfter: string | null;
  readonly blocking: boolean;
}

export interface TechnicianWorkload {
  readonly percent: number | null;
  readonly state: 'percent' | 'no_availability' | 'none';
}

export interface PlannerTechnician {
  readonly id: string;
  readonly name: string;
  readonly initials: string;
  readonly primarySkill: string | null;
  readonly slot: TechnicianSlot;
  readonly workload: TechnicianWorkload;
}

export interface AssessmentPlanner {
  readonly timezone: string;
  readonly branchId: string | null;
  readonly technicians: readonly PlannerTechnician[];
}

export interface TimeRange {
  readonly start: string;
  readonly end: string;
}

export interface CalendarDay {
  readonly date: string;
  readonly availability: readonly TimeRange[];
  readonly timeOff: readonly TimeRange[];
  readonly breaks: readonly TimeRange[];
}

export interface CalendarEvent {
  readonly kind: 'assessment' | 'visit';
  readonly start: string;
  readonly end: string;
  readonly label: string;
  readonly isCurrent: boolean;
}

export interface AssessmentCalendar {
  readonly timezone: string;
  readonly days: readonly CalendarDay[];
  readonly events: readonly CalendarEvent[];
}

export interface PlannerQuery {
  readonly date: string;
  readonly start: string;
  readonly durationMinutes: number;
  readonly branchId?: string;
}

export interface CalendarQuery {
  readonly technicianId: string;
  readonly from: string;
  readonly to: string;
}

// BR-03: actions per status.

export type RequestActionId =
  | 'schedule-assessment'
  | 'request-information'
  | 'mark-ready'
  | 'complete-assessment'
  | 'reschedule'
  | 'create-quote'
  | 'continue-quote'
  | 'view-quote'
  | 'assign'
  | 'change-priority'
  | 'set-branch'
  | 'log-response'
  | 'start-review'
  | 'move-back'
  | 'cancel-assessment'
  | 'cancel-request';

export interface RequestAction {
  readonly id: RequestActionId;
  readonly label: string;
  readonly primary?: boolean;
}

const ACTION_LABELS: Readonly<Record<RequestActionId, string>> = {
  'schedule-assessment': 'Schedule assessment',
  'request-information': 'Request information',
  'mark-ready': 'Mark ready for quote',
  'complete-assessment': 'Complete assessment',
  reschedule: 'Reschedule',
  'create-quote': 'Create quote',
  'continue-quote': 'Continue quote',
  'view-quote': 'View quote',
  assign: 'Assign',
  'change-priority': 'Change priority',
  'set-branch': 'Set branch',
  'log-response': 'Log customer response',
  'start-review': 'Start review',
  'move-back': 'Move back to review',
  'cancel-assessment': 'Cancel assessment',
  'cancel-request': 'Cancel request',
};

function actions(ids: readonly RequestActionId[], primary?: RequestActionId): RequestAction[] {
  return ids.map((id) => ({
    id,
    label: ACTION_LABELS[id],
    ...(id === primary ? { primary: true } : {}),
  }));
}

export function isOpenStatus(status: RequestStatus): status is BoardStatus {
  return (BOARD_STATUSES as readonly string[]).includes(status);
}

/** A quote that is not cancelled (BR-04, quote-builder). */
export function hasOpenQuote(quote: RequestQuote | null | undefined): boolean {
  return quote !== null && quote !== undefined && quote.status !== 'cancelled';
}

/**
 * Footer buttons: a pure function of status, the manager flag and the quote (BR-03; BR-04 of
 * quote-builder). Every role that reads quotes sees View quote on a `quoted` request.
 */
export function footerActions(
  status: RequestStatus,
  manager: boolean,
  quote: RequestQuote | null | undefined = null,
  readsQuotes = manager,
): RequestAction[] {
  if (status === 'quoted') {
    return readsQuotes && hasOpenQuote(quote) ? actions(['view-quote'], 'view-quote') : [];
  }
  if (!manager) {
    return [];
  }
  switch (status) {
    case 'new':
    case 'needs_review':
      return actions(
        ['schedule-assessment', 'request-information', 'mark-ready'],
        'schedule-assessment',
      );
    case 'assessment_scheduled':
      return actions(
        ['complete-assessment', 'request-information', 'reschedule'],
        'complete-assessment',
      );
    case 'ready_for_quote':
      return hasOpenQuote(quote)
        ? actions(['continue-quote', 'request-information'], 'continue-quote')
        : actions(['create-quote', 'request-information'], 'create-quote');
    default:
      return [];
  }
}

/** More-actions menu (BR-03). */
export function menuActions(status: RequestStatus, manager: boolean): RequestAction[] {
  if (!manager || !isOpenStatus(status)) {
    return [];
  }
  const ids: RequestActionId[] = ['assign', 'change-priority', 'set-branch', 'log-response'];
  if (status === 'new') {
    ids.push('start-review');
  }
  if (status === 'ready_for_quote') {
    ids.push('move-back');
  }
  if (status === 'assessment_scheduled') {
    ids.push('cancel-assessment');
  }
  ids.push('cancel-request');
  return actions(ids);
}

/** The internal note input and Add file are shown to managers on open requests (BR-03). */
export function canEditRequest(status: RequestStatus, manager: boolean): boolean {
  return manager && isOpenStatus(status);
}

/** One board column: its loaded cards, true total and load-more state. */
export interface ColumnState {
  readonly items: readonly RequestCard[];
  readonly total: number;
  readonly loadingMore: boolean;
  readonly moreFailed: boolean;
}

export const EMPTY_COLUMN: ColumnState = {
  items: [],
  total: 0,
  loadingMore: false,
  moreFailed: false,
};
