import {
  ActivityRow,
  InvoiceRow,
  PortalAppointment,
  QuoteRow,
  REQUEST_STATUS_LABELS,
  RequestStatus,
  VISIT_STEPS,
} from '../models/portal.model';

export type ChipTone = 'info' | 'warning' | 'success' | 'danger' | 'neutral';

export interface ChipView {
  readonly label: string;
  readonly tone: ChipTone;
}

const REQUEST_TONES: Readonly<Record<RequestStatus, ChipTone>> = {
  new: 'info',
  needs_review: 'warning',
  assessment_scheduled: 'info',
  ready_for_quote: 'info',
  quoted: 'info',
  converted: 'success',
  cancelled: 'neutral',
};

/** BR-39 customer-facing request status. */
export function requestChip(status: RequestStatus): ChipView {
  return {
    label: REQUEST_STATUS_LABELS[status] ?? status,
    tone: REQUEST_TONES[status] ?? 'neutral',
  };
}

const QUOTE_CHIPS: Readonly<Record<QuoteRow['status'], ChipView>> = {
  sent: { label: 'Awaiting your approval', tone: 'warning' },
  clarification_requested: { label: 'Question sent', tone: 'info' },
  approved: { label: 'Approved', tone: 'success' },
  rejected: { label: 'Declined', tone: 'neutral' },
  expired: { label: 'Expired', tone: 'neutral' },
};

/** BR-30 quote status labels. */
export function quoteChip(status: string): ChipView {
  return QUOTE_CHIPS[status as QuoteRow['status']] ?? { label: status, tone: 'neutral' };
}

const INVOICE_CHIPS: Readonly<Record<string, ChipView>> = {
  sent: { label: 'Sent', tone: 'info' },
  partially_paid: { label: 'Partially paid', tone: 'warning' },
  overdue: { label: 'Overdue', tone: 'danger' },
  paid: { label: 'Paid', tone: 'success' },
};

export function invoiceChip(status: InvoiceRow['status']): ChipView {
  return INVOICE_CHIPS[status] ?? { label: status, tone: 'neutral' };
}

/** BR-38 label of a visit, or "Cancelled". */
export function visitChip(visit: Pick<PortalAppointment, 'status' | 'progressStep'>): ChipView {
  if (visit.status === 'cancelled') {
    return { label: 'Cancelled', tone: 'neutral' };
  }
  const label = VISIT_STEPS[visit.progressStep] ?? visit.status;
  return { label, tone: visit.progressStep >= 3 ? 'success' : 'info' };
}

const ACTIVITY_CHIPS: Readonly<Record<ActivityRow['status'], ChipView>> = {
  completed: { label: 'Completed', tone: 'neutral' },
  paid: { label: 'Completed + Paid', tone: 'success' },
  invoice_due: { label: 'Invoice due', tone: 'danger' },
};

/** BR-26 activity statuses. */
export function activityChip(status: ActivityRow['status']): ChipView {
  return ACTIVITY_CHIPS[status];
}

/** BR-22 notice under the stepper. */
export function appointmentNotice(visit: PortalAppointment): string {
  const technician = visit.technician;
  if (technician === null) {
    return "We'll let you know once a technician is assigned.";
  }
  switch (visit.status) {
    case 'on_the_way':
      return `${technician.firstName} is on the way.`;
    case 'in_progress':
    case 'paused':
      return 'Work is in progress.';
    default:
      return `We'll let you know when ${technician.firstName} is on the way.`;
  }
}
