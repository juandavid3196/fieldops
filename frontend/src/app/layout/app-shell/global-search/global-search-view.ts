import { GlobalSearchEntityType, GlobalSearchItem } from '../../../core/models/global-search.model';
import { STATUS_LABELS as REQUEST_STATUS_LABELS } from '../../../features/requests/models/requests.model';
import { statusLabel as invoiceStatusLabel } from '../../../features/invoices/utils/invoice-format';
import { STATUS_LABELS as WORK_ORDER_STATUS_LABELS } from '../../../features/jobs/utils/work-order-format';
import { STATUS_LABELS as QUOTE_STATUS_LABELS } from '../../../features/quotes/utils/quote-format';

export type SearchTone = 'success' | 'info' | 'warning' | 'danger' | 'neutral';

export interface SearchRow {
  readonly id: string;
  readonly item: GlobalSearchItem;
  readonly icon: string;
  readonly text: string;
  readonly meta: string;
  readonly statusLabel: string;
  readonly tone: SearchTone;
  /** Accessible option name: type, number, title, subtitle, status. */
  readonly name: string;
  readonly route: string;
  readonly queryParams: Readonly<Record<string, string>> | null;
}

export interface SearchGroup {
  readonly type: GlobalSearchEntityType;
  readonly label: string;
  readonly labelId: string;
  readonly rows: readonly SearchRow[];
}

const TYPE_LABELS: Readonly<Record<GlobalSearchEntityType, string>> = {
  customer: 'Customers',
  service_request: 'Requests',
  quote: 'Quotes',
  work_order: 'Jobs',
  invoice: 'Invoices',
};

const TYPE_SINGULAR: Readonly<Record<GlobalSearchEntityType, string>> = {
  customer: 'Customer',
  service_request: 'Request',
  quote: 'Quote',
  work_order: 'Job',
  invoice: 'Invoice',
};

const TYPE_ICONS: Readonly<Record<GlobalSearchEntityType, string>> = {
  customer: 'pi-users',
  service_request: 'pi-inbox',
  quote: 'pi-file-edit',
  work_order: 'pi-wrench',
  invoice: 'pi-receipt',
};

const TONES: Readonly<Record<GlobalSearchEntityType, Readonly<Record<string, SearchTone>>>> = {
  customer: { active: 'info', inactive: 'neutral' },
  service_request: {
    new: 'info',
    needs_review: 'warning',
    assessment_scheduled: 'info',
    ready_for_quote: 'info',
    quoted: 'info',
    converted: 'success',
    cancelled: 'neutral',
  },
  quote: {
    draft: 'neutral',
    sent: 'info',
    clarification_requested: 'warning',
    approved: 'success',
    rejected: 'danger',
    expired: 'neutral',
    cancelled: 'neutral',
  },
  work_order: { draft: 'neutral', ready_to_schedule: 'warning', scheduled: 'info' },
  invoice: {
    draft: 'neutral',
    sent: 'info',
    partially_paid: 'warning',
    paid: 'success',
    overdue: 'danger',
    void: 'neutral',
  },
};

function lookup(map: Readonly<Record<string, string>>, status: string): string {
  return Object.hasOwn(map, status) ? map[status] : status;
}

/** Status label of the entity's own list; an unknown code is shown as is, never backend text. */
export function statusLabelOf(type: GlobalSearchEntityType, status: string | null): string {
  if (status === null) {
    return '';
  }
  switch (type) {
    case 'customer':
      return status === 'active' ? 'Customer' : status === 'inactive' ? 'Inactive' : status;
    case 'service_request':
      return lookup(REQUEST_STATUS_LABELS, status);
    case 'quote':
      return lookup(QUOTE_STATUS_LABELS, status);
    case 'work_order':
      return lookup(WORK_ORDER_STATUS_LABELS, status);
    case 'invoice':
      return invoiceStatusLabel(status);
  }
}

/** BR-15: the only place a result route is built, from `entityType` and `entityId`. */
export function routeOf(
  type: GlobalSearchEntityType,
  id: string,
): { route: string; queryParams: Record<string, string> | null } {
  const segment = encodeURIComponent(id);
  switch (type) {
    case 'customer':
      return { route: `/customers/${segment}`, queryParams: null };
    case 'service_request':
      return { route: '/requests', queryParams: { request: id } };
    case 'quote':
      return { route: `/quotes/${segment}`, queryParams: null };
    case 'work_order':
      return { route: `/jobs/${segment}`, queryParams: null };
    case 'invoice':
      return { route: `/invoices/${segment}`, queryParams: null };
  }
}

/** Groups by type at the position of each type's first item; options get stable sequential ids. */
export function buildGroups(items: readonly GlobalSearchItem[]): readonly SearchGroup[] {
  const order: GlobalSearchEntityType[] = [];
  for (const item of items) {
    if (!order.includes(item.entityType)) {
      order.push(item.entityType);
    }
  }
  let index = 0;
  return order.map((type) => ({
    type,
    label: TYPE_LABELS[type],
    labelId: `global-search-group-${type}`,
    rows: items
      .filter((item) => item.entityType === type)
      .map((item) => {
        const text = item.displayNumber ? `${item.displayNumber} · ${item.title}` : item.title;
        const meta = [item.subtitle, item.branchName].filter((part) => !!part).join(' · ');
        const label = statusLabelOf(type, item.status);
        const { route, queryParams } = routeOf(type, item.entityId);
        return {
          id: `global-search-option-${index++}`,
          item,
          icon: TYPE_ICONS[type],
          text,
          meta,
          statusLabel: label,
          tone: (item.status !== null && Object.hasOwn(TONES[type], item.status)
            ? TONES[type][item.status]
            : 'neutral') as SearchTone,
          name: [TYPE_SINGULAR[type], text, meta, label].filter((part) => part !== '').join(', '),
          route,
          queryParams,
        };
      }),
  }));
}
