/** Fixed copy of the quotes workspace (specs/quotes-management/spec.md UI states; backend text is never shown). */
export const QUOTES_LOAD_ERROR_MESSAGE = "We couldn't load quotes. Try again.";
export const QUOTES_METRICS_ERROR_MESSAGE = "We couldn't load quote metrics. Try again.";
export const QUOTES_EMPTY_MESSAGE =
  'No quotes yet. Quotes are created from requests that are ready for a quote.';
export const QUOTES_FILTERED_EMPTY_MESSAGE = 'No quotes match the current view.';
export const QUOTES_NO_BRANCHES_MESSAGE = 'No branches available.';

/** BR-10: rows per page offered by the UI (the first is the default). */
export const QUOTES_PAGE_SIZES: readonly number[] = [10, 25, 50];
/** BR-19: URL parameters owned by the workspace. */
export const QUOTES_QUERY_KEYS = [
  'tab',
  'q',
  'branch',
  'status',
  'date',
  'sort',
  'page',
  'pageSize',
] as const;

export type QuotesTab = 'all' | 'drafts' | 'sent' | 'approved' | 'closed';
export type QuotesDate = 'any' | 'today' | '7d' | '30d' | '90d';
export type QuotesSort = 'updated' | 'newest' | 'oldest' | 'total' | 'expiring';

/** BR-03 display status codes (the API returns the code; the frontend owns label and tone). */
export type QuoteDisplayStatus =
  | 'draft'
  | 'awaiting_response'
  | 'clarification_requested'
  | 'expired'
  | 'approved'
  | 'rejected'
  | 'cancelled';

export type QuoteActionCode =
  | 'view'
  | 'continue_draft'
  | 'continue_revision'
  | 'revise'
  | 'create_work_order'
  | 'continue_work_order'
  | 'view_job'
  | 'open_request';

export interface QuotesOptions {
  readonly branches: readonly { readonly id: string; readonly name: string }[];
  readonly organizationTimezone: string;
  readonly currency: string;
  readonly canManage: boolean;
  readonly canManageWorkOrders: boolean;
  readonly canReadWorkOrders: boolean;
}

interface Amount {
  readonly count: number;
  readonly amount: number;
}

export interface QuotesSummary {
  readonly currency: string;
  readonly drafts: Amount;
  readonly awaitingResponse: Amount;
  readonly approvedThisMonth: Amount;
  readonly needsAttention: { readonly count: number };
  readonly readyRequests: number;
}

export interface QuoteRow {
  readonly id: string;
  readonly displayNumber: string;
  readonly revisionNo: number;
  readonly revisionInProgress: boolean;
  readonly customerName: string;
  readonly property: {
    readonly name: string;
    readonly postalCode: string | null;
    readonly city: string | null;
  } | null;
  readonly request: { readonly id: string; readonly displayNumber: string; readonly title: string };
  readonly branch: { readonly id: string; readonly name: string } | null;
  readonly total: number;
  readonly currency: string;
  readonly status: QuoteDisplayStatus;
  readonly sentAt: string | null;
  readonly validUntil: string | null;
  readonly respondedAt: string | null;
  readonly updatedAt: string;
  readonly createdAt: string;
  readonly workOrderId: string | null;
  readonly actions: readonly QuoteActionCode[];
}

export interface QuotesPage {
  readonly items: readonly QuoteRow[];
  readonly total: number;
  readonly counts: Readonly<Record<QuotesTab, number>>;
}

/** Normalized URL state of `/quotes` (BR-19); `branch` is null for the default branch. */
export interface QuotesView {
  readonly tab: QuotesTab;
  readonly q: string;
  readonly branch: string | null;
  readonly status: QuoteDisplayStatus | null;
  readonly date: QuotesDate;
  readonly sort: QuotesSort;
  readonly page: number;
  readonly pageSize: number;
}

/** Query of `GET /quotes` (the effective branch id is resolved by the page). */
export interface QuotesRequest {
  readonly tab: QuotesTab;
  readonly q: string;
  readonly branchId: string | null;
  readonly status: QuoteDisplayStatus | null;
  readonly date: QuotesDate;
  readonly sort: QuotesSort;
  readonly page: number;
  readonly pageSize: number;
}
