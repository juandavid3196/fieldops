import {
  ActivityRow,
  InvoiceRow,
  PortalAppointment,
  QuoteRow,
  REQUEST_STATUS_LABELS,
  RequestStatus,
  VISIT_STEPS,
  progressIndex,
} from '../models/portal.model';

import { localized, t } from '../../../core/i18n/translate';
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

const QUOTE_CHIPS: Readonly<Record<QuoteRow['status'], ChipView>> = localized(() => ({
  sent: { label: t('portal.portalStatus.awaitingYourApproval'), tone: 'warning' },
  clarification_requested: { label: t('portal.portalStatus.questionSent'), tone: 'info' },
  approved: { label: t('portal.portalStatus.approved'), tone: 'success' },
  rejected: { label: t('portal.portalStatus.declined'), tone: 'neutral' },
  expired: { label: t('portal.portalStatus.expired'), tone: 'neutral' },
}));

/** BR-30 quote status labels. */
export function quoteChip(status: string): ChipView {
  return QUOTE_CHIPS[status as QuoteRow['status']] ?? { label: status, tone: 'neutral' };
}

const INVOICE_CHIPS: Readonly<Record<string, ChipView>> = localized(() => ({
  sent: { label: t('portal.portalStatus.sent'), tone: 'info' },
  partially_paid: { label: t('portal.portalStatus.partiallyPaid'), tone: 'warning' },
  overdue: { label: t('portal.portalStatus.overdue'), tone: 'danger' },
  paid: { label: t('portal.portalStatus.paid'), tone: 'success' },
}));

export function invoiceChip(status: InvoiceRow['status']): ChipView {
  return INVOICE_CHIPS[status] ?? { label: status, tone: 'neutral' };
}

/** BR-38 label of a visit, or "Cancelled". */
export function visitChip(visit: Pick<PortalAppointment, 'status' | 'progressStep'>): ChipView {
  if (visit.status === 'cancelled') {
    return { label: t('portal.portalStatus.cancelled'), tone: 'neutral' };
  }
  const index = progressIndex(visit.progressStep);
  return { label: VISIT_STEPS[index], tone: index >= 3 ? 'success' : 'info' };
}

const ACTIVITY_CHIPS: Readonly<Record<ActivityRow['status'], ChipView>> = localized(() => ({
  completed: { label: t('portal.portalStatus.completed'), tone: 'neutral' },
  paid: { label: t('portal.portalStatus.completedPaid'), tone: 'success' },
  invoice_due: { label: t('portal.portalStatus.invoiceDue'), tone: 'danger' },
}));

/** BR-26 activity statuses. */
export function activityChip(status: ActivityRow['status']): ChipView {
  return ACTIVITY_CHIPS[status];
}

/** BR-22 notice under the stepper. */
export function appointmentNotice(visit: PortalAppointment): string {
  const technician = visit.technician;
  if (technician === null) {
    return t('portal.portalStatus.wellLetYouKnow');
  }
  switch (visit.status) {
    case 'on_the_way':
      return t('portal.portalStatus.isOnTheWay', { firstName: technician.firstName });
    case 'in_progress':
    case 'paused':
      return t('portal.portalStatus.workIsInProgress');
    default:
      return t('portal.portalStatus.wellLetYouKnow2', { firstName: technician.firstName });
  }
}
