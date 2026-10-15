import { formatMoney } from '../../customers/utils/customer-detail-format';
import { ChipTone, fullDateTime, monthDay, updatedText } from '../../jobs/utils/jobs-format';
import {
  QUOTES_PAGE_SIZES,
  QUOTES_QUERY_KEYS,
  QuoteActionCode,
  QuoteDisplayStatus,
  QuoteRow,
  QuotesDate,
  QuotesSort,
  QuotesTab,
  QuotesView,
} from '../models/quotes-list.model';

export { fullDateTime, updatedText };

/** BR-03: code -> fixed label and chip tone (the API never sends labels). */
export const QUOTE_STATUS_CHIPS: Readonly<
  Record<QuoteDisplayStatus, { readonly label: string; readonly tone: ChipTone }>
> = {
  draft: { label: 'Draft', tone: 'neutral' },
  awaiting_response: { label: 'Awaiting response', tone: 'info' },
  clarification_requested: { label: 'Clarification requested', tone: 'warning' },
  expired: { label: 'Expired', tone: 'neutral' },
  approved: { label: 'Approved', tone: 'success' },
  rejected: { label: 'Rejected', tone: 'danger' },
  cancelled: { label: 'Cancelled', tone: 'neutral' },
};

/** BR-06: status codes allowed per tab. */
export const TAB_STATUSES: Readonly<Record<QuotesTab, readonly QuoteDisplayStatus[]>> = {
  all: Object.keys(QUOTE_STATUS_CHIPS) as QuoteDisplayStatus[],
  drafts: ['draft'],
  sent: ['awaiting_response', 'clarification_requested'],
  approved: ['approved'],
  closed: ['rejected', 'expired', 'cancelled'],
};

export const QUOTES_TABS: readonly { readonly code: QuotesTab; readonly label: string }[] = [
  { code: 'all', label: 'All quotes' },
  { code: 'drafts', label: 'Drafts' },
  { code: 'sent', label: 'Sent' },
  { code: 'approved', label: 'Approved' },
  { code: 'closed', label: 'Closed' },
];

export const QUOTES_DATES: readonly { readonly code: QuotesDate; readonly label: string }[] = [
  { code: 'any', label: 'Any date' },
  { code: 'today', label: 'Today' },
  { code: '7d', label: 'Last 7 days' },
  { code: '30d', label: 'Last 30 days' },
  { code: '90d', label: 'Last 90 days' },
];

export const QUOTES_SORTS: readonly { readonly code: QuotesSort; readonly label: string }[] = [
  { code: 'updated', label: 'Recently updated' },
  { code: 'newest', label: 'Newest' },
  { code: 'oldest', label: 'Oldest' },
  { code: 'total', label: 'Highest total' },
  { code: 'expiring', label: 'Expiring soonest' },
];

const MAX_SEARCH_LENGTH = 100;

function positiveInteger(value: string | null): number | null {
  if (value === null || !/^\d{1,9}$/.test(value)) {
    return null;
  }
  const parsed = Number(value);
  return parsed >= 1 ? parsed : null;
}

/**
 * BR-19: the valid view of the raw URL parameters; every unknown or invalid value takes its default.
 * `branchIds` is null until the options are known; `defaultBranchId` is the branch whose URL form is
 * "absent".
 */
export function resolveQuotesView(
  get: (key: string) => string | null,
  branchIds: readonly string[] | null,
  defaultBranchId: string | null,
): QuotesView {
  const tab = QUOTES_TABS.find((candidate) => candidate.code === get('tab'))?.code ?? 'all';
  const term = (get('q') ?? '').trim();
  const rawBranch = get('branch');
  const branch =
    rawBranch === null ||
    rawBranch === '' ||
    rawBranch === defaultBranchId ||
    (branchIds !== null && !branchIds.includes(rawBranch))
      ? null
      : rawBranch;
  const pageSize = positiveInteger(get('pageSize'));
  return {
    tab,
    q: term.length > MAX_SEARCH_LENGTH ? '' : term,
    branch,
    status: TAB_STATUSES[tab].find((code) => code === get('status')) ?? null,
    date: QUOTES_DATES.find((candidate) => candidate.code === get('date'))?.code ?? 'any',
    sort: QUOTES_SORTS.find((candidate) => candidate.code === get('sort'))?.code ?? 'updated',
    page: positiveInteger(get('page')) ?? 1,
    pageSize: QUOTES_PAGE_SIZES.find((size) => size === pageSize) ?? QUOTES_PAGE_SIZES[0],
  };
}

/** Canonical query parameters of a view; defaults are null (omitted from the URL). */
export function quotesViewParams(view: QuotesView): Record<string, string | null> {
  return {
    tab: view.tab === 'all' ? null : view.tab,
    q: view.q === '' ? null : view.q,
    branch: view.branch,
    status: view.status,
    date: view.date === 'any' ? null : view.date,
    sort: view.sort === 'updated' ? null : view.sort,
    page: view.page === 1 ? null : String(view.page),
    pageSize: view.pageSize === QUOTES_PAGE_SIZES[0] ? null : String(view.pageSize),
  };
}

/** True when the raw URL already is the canonical form of its view. */
export function isCanonicalQuotesUrl(
  get: (key: string) => string | null,
  view: QuotesView,
): boolean {
  const canonical = quotesViewParams(view);
  return QUOTES_QUERY_KEYS.every((key) => (get(key) ?? null) === canonical[key]);
}

/** BR-19: the badge of Clear filters counts the active search, status and date. */
export function activeFilterCount(view: QuotesView): number {
  return (view.q !== '' ? 1 : 0) + (view.status !== null ? 1 : 0) + (view.date !== 'any' ? 1 : 0);
}

// Row content (BR-13)

/** "<property name> · <postal code> <city>" or "No property". */
export function propertyText(row: QuoteRow): string {
  const property = row.property;
  if (property === null) {
    return 'No property';
  }
  const location = [property.postalCode, property.city]
    .filter((part) => part !== null && part !== '')
    .join(' ');
  return location === '' ? property.name : `${property.name} · ${location}`;
}

/** A yyyy-MM-dd calendar date as "MMM d" (", yyyy" outside the current organization year), with no zone shift. */
function dateOnly(value: string, timeZone: string, now: Date): string {
  const [year, month, day] = value.slice(0, 10).split('-').map(Number);
  const currentYear = new Intl.DateTimeFormat('en-US', { timeZone, year: 'numeric' }).format(now);
  return new Intl.DateTimeFormat('en-US', {
    timeZone: 'UTC',
    month: 'short',
    day: 'numeric',
    ...(String(year) === currentYear ? {} : { year: 'numeric' }),
  }).format(new Date(Date.UTC(year, month - 1, day)));
}

/** BR-13 "Sent / valid until" text per status; dates in the organization time zone. */
export function sentText(row: QuoteRow, timeZone: string, now: Date): string {
  const date = (iso: string | null): string =>
    iso === null ? '' : monthDay(new Date(iso), timeZone, now);
  const calendarDate = (value: string | null): string =>
    value === null ? '' : dateOnly(value, timeZone, now);
  switch (row.status) {
    case 'draft':
      return 'Draft · Not sent';
    case 'awaiting_response':
    case 'clarification_requested':
      return `Sent ${date(row.sentAt)} · Valid until ${calendarDate(row.validUntil)}`;
    case 'approved':
      return `Approved ${date(row.respondedAt)}`;
    case 'rejected':
      return `Rejected ${date(row.respondedAt)}`;
    case 'expired':
      return `Expired ${calendarDate(row.validUntil)}`;
    case 'cancelled':
      return `Cancelled ${date(row.updatedAt)}`;
  }
}

export const totalText = (row: QuoteRow): string => formatMoney(row.total, row.currency);

// Row actions (BR-15)

export interface QuoteActionTarget {
  readonly code: QuoteActionCode;
  readonly label: string;
  /** Null for `revise`, which calls the API instead of navigating. */
  readonly commands: readonly string[] | null;
  readonly queryParams: Readonly<Record<string, string>> | null;
}

/** The actions of a row, as the server sent them (in order), with the BR-15 destinations. */
export function quoteActionTargets(row: QuoteRow): QuoteActionTarget[] {
  const targets: QuoteActionTarget[] = [];
  const add = (
    code: QuoteActionCode,
    label: string,
    commands: readonly string[] | null,
    queryParams: Readonly<Record<string, string>> | null = null,
  ): void => {
    targets.push({ code, label, commands, queryParams });
  };
  for (const code of row.actions) {
    switch (code) {
      case 'view':
        add(code, 'View quote', ['/quotes', row.id]);
        break;
      case 'continue_draft':
        add(code, 'Continue draft', ['/quotes', row.id, 'edit']);
        break;
      case 'continue_revision':
        add(code, 'Continue revision', ['/quotes', row.id, 'edit']);
        break;
      case 'revise':
        add(code, 'Revise quote', null);
        break;
      case 'create_work_order':
        add(code, 'Create work order', ['/quotes', row.id, 'work-order']);
        break;
      case 'continue_work_order':
        add(code, 'Continue work order', ['/quotes', row.id, 'work-order']);
        break;
      case 'view_job':
        if (row.workOrderId !== null) {
          add(code, 'View job', ['/jobs', row.workOrderId]);
        }
        break;
      case 'open_request':
        add(code, 'Open request', ['/requests'], { request: row.request.id });
        break;
    }
  }
  return targets;
}

/** The inline button: Continue (`continue_draft`) when present, else View. */
export function inlineAction(row: QuoteRow): QuoteActionTarget {
  const targets = quoteActionTargets(row);
  return (
    targets.find((target) => target.code === 'continue_draft') ??
    targets.find((target) => target.code === 'view') ?? {
      code: 'view',
      label: 'View quote',
      commands: ['/quotes', row.id],
      queryParams: null,
    }
  );
}

/** BR-18 notice copy. */
export function readyNoticeText(count: number): string {
  return count === 1
    ? '1 service request is ready for a quote.'
    : `${count} service requests are ready for a quote.`;
}
