import { JOB_STATUS_CHIPS } from '../../jobs/utils/jobs-format';
import { ActivityItem, ActivityType, RecordLinkKind } from '../models/customer.model';
import { formatMoney, sentenceCase } from './customer-detail-format';
import { formatDate, formatMonthDay, formatTime } from './customer-format';

import { t } from '../../../core/i18n/translate';

export type ActivityTone = 'neutral' | 'info' | 'purple' | 'warning' | 'success';

/** Type icon and tone of a timeline event (BR-11). */
export const ACTIVITY_TYPE_STYLE: Readonly<
  Record<ActivityType, { readonly icon: string; readonly tone: ActivityTone }>
> = {
  customer: { icon: 'pi-user', tone: 'neutral' },
  requests: { icon: 'pi-inbox', tone: 'info' },
  messages: { icon: 'pi-comment', tone: 'info' },
  quotes: { icon: 'pi-file', tone: 'purple' },
  jobs: { icon: 'pi-wrench', tone: 'warning' },
  visits: { icon: 'pi-calendar', tone: 'warning' },
  invoices: { icon: 'pi-receipt', tone: 'info' },
  payments: { icon: 'pi-credit-card', tone: 'success' },
};

/** Catalog action -> title key (`customers.activityTab.title.*`); customer actions reuse BR-17 labels. */
const TITLE_KEYS: Readonly<Record<string, string>> = {
  'service_request.created': 'requestCreated',
  'service_request.assessment_scheduled': 'assessmentScheduled',
  'service_request.assessment_rescheduled': 'assessmentRescheduled',
  'service_request.assessment_cancelled': 'assessmentCancelled',
  'service_request.assessment_completed': 'assessmentCompleted',
  'service_request.information_requested': 'informationRequested',
  'service_request.customer_response_logged': 'customerResponseLogged',
  'service_request.quoted': 'requestQuoted',
  'portal.message_sent': 'customerMessage',
  'quote.sent': 'quoteSent',
  'quote.revised': 'quoteRevised',
  'quote.approved': 'quoteApproved',
  'quote.rejected': 'quoteRejected',
  'quote.clarification_requested': 'clarificationRequested',
  'quote.expired': 'quoteExpired',
  'quote.cancelled': 'quoteCancelled',
  'work_order.created': 'jobCreated',
  'visit.dispatched': 'jobScheduled',
  'visit.completed': 'visitCompleted',
  'visit.cancelled': 'visitCancelled',
  'visit.reschedule_requested': 'rescheduleRequested',
  'invoice.sent': 'invoiceSent',
  'invoice.email_resent': 'invoiceEmailResent',
  'invoice.bank_transfer_reported': 'bankTransferReported',
  'payment.recorded': 'paymentReceived',
  'payment.refunded': 'paymentRefunded',
};

/** Localized title from the action code (BR-11). */
export function activityTitle(item: Pick<ActivityItem, 'action' | 'subject'>): string {
  const { action, subject } = item;
  const name = subject.propertyName?.trim() || '—';
  switch (action) {
    case 'customer.created':
      return t('customers.customerDetailFormat.customerCreated');
    case 'customer.updated':
      return t('customers.customerDetailFormat.customerDetailsUpdated');
    case 'customer.archived':
      return t('customers.customerDetailFormat.customerArchived');
    case 'customer.reactivated':
      return t('customers.customerDetailFormat.customerReactivated');
    case 'property.created':
      return t('customers.customerDetailFormat.propertyAdded', { name });
    case 'property.updated':
      return t('customers.customerDetailFormat.propertyUpdated', { name });
    case 'property.primary_changed':
      return t('customers.customerDetailFormat.primaryPropertyChangedTo', { name });
    case 'property.archived':
      return t('customers.customerDetailFormat.propertyArchived', { name });
    case 'property.reactivated':
      return t('customers.customerDetailFormat.propertyReactivated', { name });
    case 'customer_note.created':
      return t('customers.customerDetailFormat.internalNoteAdded');
    case 'work_order.status_changed': {
      const status = subject.newStatus;
      return status && Object.hasOwn(JOB_STATUS_CHIPS, status)
        ? t('customers.activityTab.title.jobStatus', {
            status: JOB_STATUS_CHIPS[status as keyof typeof JOB_STATUS_CHIPS].label,
          })
        : t('customers.activityTab.title.jobStatusUpdated');
    }
    default: {
      const key = TITLE_KEYS[action];
      return key !== undefined
        ? t(`customers.activityTab.title.${key}`)
        : /^[a-z_]+\.[a-z_]+$/.test(action)
          ? sentenceCase(action.replace('.', ' '))
          : action;
    }
  }
}

const join = (...parts: readonly (string | null | undefined)[]): string | null => {
  const text = parts.filter((part): part is string => !!part && part.trim().length > 0).join(' · ');
  return text.length > 0 ? text : null;
};

/** Summary line of the catalog (BR-11); `null` when the event has none. */
export function activitySummary(
  item: ActivityItem,
  currency: string,
  timeZone: string,
): string | null {
  const s = item.subject;
  const money = typeof s.amount === 'number' ? formatMoney(s.amount, currency) : null;
  switch (item.type) {
    case 'customer':
      return null;
    case 'requests':
      return join(s.number, s.service);
    case 'messages':
      return s.contactName?.trim()
        ? t('customers.activityTab.contactSentMessage', { name: s.contactName.trim() })
        : t('customers.activityTab.aContactSentMessage');
    case 'quotes':
    case 'invoices':
      return join(s.number, money);
    case 'jobs':
      return join(s.number, s.title);
    case 'visits':
      if (item.action === 'visit.dispatched') {
        return s.scheduledAt
          ? join(
              s.number,
              t('customers.activityTab.dateAtTime', {
                date: formatMonthDay(s.scheduledAt, timeZone),
                time: formatTime(s.scheduledAt, timeZone),
              }),
            )
          : join(s.number);
      }
      return join(
        s.number,
        s.visitNumber === null || s.visitNumber === undefined
          ? null
          : t('customers.activityTab.visitNumber', { number: s.visitNumber }),
      );
    case 'payments': {
      const invoices = (s.invoiceNumbers ?? []).join(', ');
      return item.action === 'payment.refunded'
        ? t('customers.activityTab.refundOn', { invoices })
        : t('customers.activityTab.amountReceivedFor', { amount: money ?? '', invoices });
    }
  }
}

export interface ActivityView {
  readonly id: string;
  readonly icon: string;
  readonly tone: ActivityTone;
  readonly title: string;
  readonly summary: string | null;
  readonly time: string;
  readonly origin: string;
  readonly link: { readonly kind: RecordLinkKind; readonly id: string } | null;
}

export interface ActivityGroup {
  readonly key: string;
  readonly label: string;
  readonly items: readonly ActivityView[];
}

/** BR-13: portal -> "Customer portal"; staff -> the user's name; otherwise "System". */
export function activityOrigin(origin: ActivityItem['origin']): string {
  if (origin.kind === 'customer_portal') {
    return t('customers.activityTab.customerPortal');
  }
  return (
    (origin.kind === 'staff' ? origin.actorName?.trim() : '') ||
    t('customers.customerDetail.system')
  );
}

function dayKey(instant: Date, timeZone: string): string {
  const build = (zone: string): string =>
    new Intl.DateTimeFormat('en-CA', {
      timeZone: zone,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
    }).format(instant);
  try {
    return build(timeZone);
  } catch {
    return build('UTC');
  }
}

/** BR-12: events (already newest first) under "Today", "Yesterday" or "MMM d, yyyy" in the org timezone. */
export function groupActivity(
  items: readonly ActivityItem[],
  currency: string,
  timeZone: string,
  now: Date = new Date(),
): readonly ActivityGroup[] {
  const today = dayKey(now, timeZone);
  const yesterday = dayKey(new Date(now.getTime() - 24 * 60 * 60 * 1000), timeZone);
  const groups: { key: string; label: string; items: ActivityView[] }[] = [];
  for (const item of items) {
    const key = dayKey(new Date(item.occurredAt), timeZone);
    let group = groups.at(-1);
    if (group?.key !== key) {
      group = {
        key,
        label:
          key === today
            ? t('customers.activityTab.today')
            : key === yesterday
              ? t('customers.activityTab.yesterday')
              : formatDate(item.occurredAt, timeZone),
        items: [],
      };
      groups.push(group);
    }
    group.items.push({
      id: item.id,
      icon: ACTIVITY_TYPE_STYLE[item.type]?.icon ?? 'pi-clock',
      tone: ACTIVITY_TYPE_STYLE[item.type]?.tone ?? 'neutral',
      title: activityTitle(item),
      summary: activitySummary(item, currency, timeZone),
      time: formatTime(item.occurredAt, timeZone),
      origin: activityOrigin(item.origin),
      link: item.link ?? null,
    });
  }
  return groups;
}
