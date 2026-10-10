/** Portal API shapes (customer-portal-dashboard API contracts). Dates are `YYYY-MM-DD`, times `HH:mm`. */

export interface Page<T> {
  readonly items: readonly T[];
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface PortalProperty {
  readonly id: string;
  readonly name: string;
  readonly addressLine1: string;
  readonly addressLine2: string | null;
  readonly city: string;
  readonly stateRegion: string;
  readonly postalCode: string;
  readonly countryCode: string;
  readonly accessInstructions: string | null;
  readonly isPrimary: boolean;
  readonly lastServiceOn: string | null;
  /** Concurrency value echoed by `PATCH`. */
  readonly updatedAt: string;
}

export interface CreatePropertyBody {
  readonly name: string;
  readonly addressLine1: string;
  readonly addressLine2: string;
  readonly city: string;
  readonly stateRegion: string;
  readonly postalCode: string;
  readonly countryCode: 'US';
  readonly accessInstructions: string;
}

export interface UpdatePropertyBody {
  readonly name: string;
  readonly accessInstructions: string | null;
  readonly isPrimary: boolean;
  readonly updatedAt: string;
}

export type RequestStatus =
  | 'new'
  | 'needs_review'
  | 'assessment_scheduled'
  | 'ready_for_quote'
  | 'quoted'
  | 'converted'
  | 'cancelled';

export interface RequestRow {
  readonly id: string;
  readonly displayNumber: string;
  readonly title: string;
  readonly propertyName: string | null;
  readonly status: RequestStatus;
  readonly statusLabel: string;
  readonly submittedOn: string;
}

export type TimeWindow = 'morning' | 'afternoon' | 'evening' | 'any';

export interface RequestAvailability {
  readonly mode: 'asap' | 'date' | 'flexible';
  readonly dates: readonly { readonly date: string; readonly window: TimeWindow }[];
  readonly window: TimeWindow;
  readonly notes: string | null;
}

export interface RequestDetail extends RequestRow {
  readonly categoryName: string;
  readonly serviceName: string | null;
  readonly description: string;
  readonly urgency: string;
  readonly hasActiveDamage: boolean;
  readonly property: Omit<PortalProperty, 'lastServiceOn' | 'updatedAt'> | null;
  readonly availability: RequestAvailability | null;
  readonly quote: {
    readonly id: string;
    readonly displayNumber: string;
    readonly status: string;
  } | null;
}

export type VisitStatus =
  | 'scheduled'
  | 'assigned'
  | 'on_the_way'
  | 'in_progress'
  | 'paused'
  | 'completed'
  | 'needs_correction'
  | 'approved'
  | 'cancelled';

export interface PortalAppointment {
  readonly visitId: string;
  readonly workOrderId: string;
  readonly workOrderNumber: string;
  readonly title: string;
  readonly date: string;
  readonly startTime: string;
  readonly endTime: string;
  readonly arrivalWindow: { readonly start: string; readonly end: string } | null;
  readonly technician: { readonly fullName: string; readonly firstName: string } | null;
  readonly property: { readonly name: string; readonly addressLine1: string };
  readonly status: VisitStatus;
  /** 0 Scheduled, 1 On the way, 2 In progress, 3 Completed (BR-38). */
  readonly progressStep: number;
  readonly canRequestReschedule: boolean;
  readonly rescheduleRequestedOn: string | null;
  readonly reportAvailable: boolean;
}

export interface RescheduleRequestBody {
  readonly preferredDate: string;
  readonly timeWindow: TimeWindow;
  readonly reason: string;
}

export interface QuoteRow {
  readonly id: string;
  readonly displayNumber: string;
  readonly scope: string;
  readonly propertyName: string | null;
  readonly total: number;
  readonly currency: string;
  readonly status: 'sent' | 'clarification_requested' | 'approved' | 'rejected' | 'expired';
  readonly validUntil: string | null;
}

export interface InvoiceRow {
  readonly id: string;
  readonly displayNumber: string;
  readonly title: string;
  readonly issueDate: string;
  readonly dueDate: string | null;
  readonly total: number;
  readonly balanceDue: number;
  readonly currency: string;
  readonly status: string;
}

export type UpdateKind = 'quote' | 'appointment' | 'invoice';

export interface PortalUpdate {
  readonly type: string;
  readonly title: string;
  readonly subtitle: string;
  readonly occurredAt: string;
  readonly isUnread: boolean;
  readonly target: { readonly kind: UpdateKind; readonly id: string };
}

export interface UpdatesResponse {
  readonly items: readonly PortalUpdate[];
  readonly unreadCount: number;
}

export interface ActivityRow {
  readonly workOrderId: string;
  readonly title: string;
  readonly completedOn: string;
  readonly reference: string;
  readonly amount: number | null;
  readonly currency: string;
  readonly status: 'completed' | 'paid' | 'invoice_due';
  readonly invoiceId: string | null;
  readonly receiptPaymentId: string | null;
}

export interface DashboardProperty {
  readonly id: string;
  readonly name: string;
  readonly addressLine1: string;
  readonly addressLine2: string | null;
  readonly city: string;
  readonly state: string;
  readonly postalCode: string;
  readonly isPrimary: boolean;
  readonly lastServiceOn: string | null;
}

export interface ActionQuote {
  readonly id: string;
  readonly displayNumber: string;
  readonly scope: string;
  readonly total: number;
  readonly currency: string;
  readonly validUntil: string | null;
  readonly status: 'sent' | 'clarification_requested';
  readonly moreCount: number;
}

export interface PaymentDue {
  readonly id: string;
  readonly displayNumber: string;
  readonly title: string;
  readonly balanceDue: number;
  readonly currency: string;
  readonly dueDate: string | null;
  readonly isOverdue: boolean;
  readonly moreCount: number;
}

export interface Dashboard {
  readonly organization: {
    readonly name: string;
    readonly phone: string | null;
    readonly canReceiveMessages: boolean;
  };
  readonly properties: readonly DashboardProperty[];
  readonly selectedPropertyId: string | null;
  readonly actionQuote: ActionQuote | null;
  readonly paymentDue: PaymentDue | null;
  readonly upcomingAppointment: PortalAppointment | null;
  readonly activeRequests: readonly RequestRow[];
  readonly updates: UpdatesResponse;
  readonly recentActivity: readonly ActivityRow[];
}

export type AppointmentScope = 'upcoming' | 'past';

/** Body of `POST /portal/service-requests` (BR-28): contact and customer come from the session. */
export interface PortalNewProperty {
  readonly propertyType: 'home' | 'business';
  readonly addressLine1: string;
  readonly addressLine2: string;
  readonly city: string;
  readonly state: string;
  readonly postalCode: string;
  readonly accessInstructions: string;
}

export interface PortalRequestCreated {
  readonly requestId: string;
  readonly requestNumber: string;
}

/** Fixed copy (backend text is never displayed). */
export const ITEM_UNAVAILABLE_MESSAGE = "This item isn't available.";
export const GENERIC_ERROR_MESSAGE = 'Something went wrong. Try again.';
export const RATE_LIMITED_MESSAGE = 'Too many attempts. Please wait a few minutes and try again.';
export const SESSION_ENDED_MESSAGE = 'Your session ended. Sign in again.';

export const REQUEST_STATUS_LABELS: Readonly<Record<RequestStatus, string>> = {
  new: 'Submitted',
  needs_review: 'Under review',
  assessment_scheduled: 'Assessment scheduled',
  ready_for_quote: 'Preparing quote',
  quoted: 'Quote ready',
  converted: 'Job created',
  cancelled: 'Cancelled',
};

export const VISIT_STEPS: readonly string[] = [
  'Scheduled',
  'On the way',
  'In progress',
  'Completed',
];
