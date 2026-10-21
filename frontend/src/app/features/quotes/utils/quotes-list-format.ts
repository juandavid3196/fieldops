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

import { localized, localizedList, t } from '../../../core/i18n/translate';
import { activeIntlLocale } from '../../../core/i18n/active-locale';
export { fullDateTime, updatedText };

/** BR-03: code -> fixed label and chip tone (the API never sends labels). */
export const QUOTE_STATUS_CHIPS: Readonly<
  Record<QuoteDisplayStatus, { readonly label: string; readonly tone: ChipTone }>
> = localized(() => ({
  draft: { label: t('quotes.quotesListFormat.draft'), tone: 'neutral' },
  awaiting_response: { label: t('quotes.quotesListFormat.awaitingResponse'), tone: 'info' },
  clarification_requested: {
    label: t('quotes.quotesListFormat.clarificationRequested'),
    tone: 'warning',
  },
  expired: { label: t('quotes.quotesListFormat.expired'), tone: 'neutral' },
  approved: { label: t('quotes.quotesListFormat.approved'), tone: 'success' },
  rejected: { label: t('quotes.quotesListFormat.rejected'), tone: 'danger' },
  cancelled: { label: t('quotes.quotesListFormat.cancelled'), tone: 'neutral' },
}));

/** BR-06: status codes allowed per tab. */
export const TAB_STATUSES: Readonly<Record<QuotesTab, readonly QuoteDisplayStatus[]>> = {
  all: Object.keys(QUOTE_STATUS_CHIPS) as QuoteDisplayStatus[],
  drafts: ['draft'],
  sent: ['awaiting_response', 'clarification_requested'],
  approved: ['approved'],
  closed: ['rejected', 'expired', 'cancelled'],
};

export const QUOTES_TABS: readonly { readonly code: QuotesTab; readonly label: string }[] =
  localizedList(() => [
    { code: 'all', label: t('quotes.quotesListFormat.allQuotes') },
    { code: 'drafts', label: t('quotes.quotesListFormat.drafts') },
    { code: 'sent', label: t('quotes.quotesListFormat.sent') },
    { code: 'approved', label: t('quotes.quotesListFormat.approved') },
    { code: 'closed', label: t('quotes.quotesListFormat.closed') },
  ]);

export const QUOTES_DATES: readonly { readonly code: QuotesDate; readonly label: string }[] =
  localizedList(() => [
    { code: 'any', label: t('quotes.quotesListFormat.anyDate') },
    { code: 'today', label: t('quotes.quotesListFormat.today') },
    { code: '7d', label: t('quotes.quotesListFormat.last7Days') },
    { code: '30d', label: t('quotes.quotesListFormat.last30Days') },
    { code: '90d', label: t('quotes.quotesListFormat.last90Days') },
  ]);

export const QUOTES_SORTS: readonly { readonly code: QuotesSort; readonly label: string }[] =
  localizedList(() => [
    { code: 'updated', label: t('quotes.quotesListFormat.recentlyUpdated') },
    { code: 'newest', label: t('quotes.quotesListFormat.newest') },
    { code: 'oldest', label: t('quotes.quotesListFormat.oldest') },
    { code: 'total', label: t('quotes.quotesListFormat.highestTotal') },
    { code: 'expiring', label: t('quotes.quotesListFormat.expiringSoonest') },
  ]);

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
    return t('quotes.quotesListFormat.noProperty');
  }
  const location = [property.postalCode, property.city]
    .filter((part) => part !== null && part !== '')
    .join(' ');
  return location === '' ? property.name : `${property.name} · ${location}`;
}

/** A yyyy-MM-dd calendar date as "MMM d" (", yyyy" outside the current organization year), with no zone shift. */
function dateOnly(value: string, timeZone: string, now: Date): string {
  const [year, month, day] = value.slice(0, 10).split('-').map(Number);
  const currentYear = new Intl.DateTimeFormat(activeIntlLocale(), {
    timeZone,
    year: 'numeric',
  }).format(now);
  return new Intl.DateTimeFormat(activeIntlLocale(), {
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
      return t('quotes.quotesListFormat.draftNotSent');
    case 'awaiting_response':
    case 'clarification_requested':
      return t('quotes.quotesListFormat.sentValidUntil', {
        sentAt: date(row.sentAt),
        validUntil: calendarDate(row.validUntil),
      });
    case 'approved':
      return t('quotes.quotesListFormat.approved2', { respondedAt: date(row.respondedAt) });
    case 'rejected':
      return t('quotes.quotesListFormat.rejected2', { respondedAt: date(row.respondedAt) });
    case 'expired':
      return t('quotes.quotesListFormat.expired2', { validUntil: calendarDate(row.validUntil) });
    case 'cancelled':
      return t('quotes.quotesListFormat.cancelled2', { updatedAt: date(row.updatedAt) });
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
        add(code, t('quotes.quotesListFormat.viewQuote'), ['/quotes', row.id]);
        break;
      case 'continue_draft':
        add(code, t('quotes.quotesListFormat.continueDraft'), ['/quotes', row.id, 'edit']);
        break;
      case 'continue_revision':
        add(code, t('quotes.quotesListFormat.continueRevision'), ['/quotes', row.id, 'edit']);
        break;
      case 'revise':
        add(code, t('quotes.quotesListFormat.reviseQuote'), null);
        break;
      case 'create_work_order':
        add(code, t('quotes.quotesListFormat.createWorkOrder'), ['/quotes', row.id, 'work-order']);
        break;
      case 'continue_work_order':
        add(code, t('quotes.quotesListFormat.continueWorkOrder'), [
          '/quotes',
          row.id,
          'work-order',
        ]);
        break;
      case 'view_job':
        if (row.workOrderId !== null) {
          add(code, t('quotes.quotesListFormat.viewJob'), ['/jobs', row.workOrderId]);
        }
        break;
      case 'open_request':
        add(code, t('quotes.quotesListFormat.openRequest'), ['/requests'], {
          request: row.request.id,
        });
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
      label: t('quotes.quotesListFormat.viewQuote'),
      commands: ['/quotes', row.id],
      queryParams: null,
    }
  );
}

/** BR-18 notice copy. */
export function readyNoticeText(count: number): string {
  return count === 1
    ? t('quotes.quotesListFormat.n1ServiceRequestIs')
    : t('quotes.quotesListFormat.serviceRequestsAreReady', { count: count });
}
