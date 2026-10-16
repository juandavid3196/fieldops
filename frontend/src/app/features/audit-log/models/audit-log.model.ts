/** Audit trail contracts (Final: specs/audit-trail/spec.md). */

export const AUDIT_FORBIDDEN_MESSAGE = "You don't have access to the audit trail.";
export const AUDIT_LOAD_ERROR_MESSAGE = "We couldn't load audit events.";
export const AUDIT_EMPTY_MESSAGE = 'No audit events match these filters.';
export const AUDIT_EXPORT_TOO_LARGE_MESSAGE = 'Narrow the filters to export 5,000 rows or fewer.';
export const AUDIT_EXPORT_FAILED_MESSAGE = "We couldn't export the audit log. Try again.";
export const AUDIT_DETAIL_ERROR_MESSAGE = "We couldn't load this event.";
export const AUDIT_DETAIL_GONE_MESSAGE = 'This event is no longer available.';
export const AUDIT_COPIED_MESSAGE = 'Event ID copied';
export const AUDIT_ALL_BRANCHES = 'All branches';

export const AUDIT_PAGE_SIZES: readonly number[] = [25, 50, 100];
export const AUDIT_MAX_SEARCH_LENGTH = 100;
/** BR-22: related activity widens the date range to the last 366 days (today included). */
export const AUDIT_RELATED_DAYS = 366;

export type AuditImpact = 'low' | 'medium' | 'high';
export type AuditSort = 'occurredAt' | 'impact';
export type AuditDirection = 'asc' | 'desc';

export interface AuditItem {
  readonly id: number;
  readonly eventId: string;
  readonly occurredAt: string;
  readonly actor: { readonly id: string | null; readonly name: string };
  readonly action: { readonly code: string; readonly label: string };
  readonly entity: {
    readonly type: string;
    readonly label: string;
    readonly id: string | null;
  };
  readonly reference: string;
  readonly summary: string;
  readonly branch: { readonly id: string; readonly name: string } | null;
  readonly source: string;
  readonly ipAddress: string | null;
  readonly impact: AuditImpact;
}

export interface AuditPage {
  readonly items: readonly AuditItem[];
  readonly total: number;
  readonly page: number;
  readonly pageSize: number;
  readonly timeZone: string;
}

export interface AuditSummary {
  readonly eventsToday: number;
  readonly activeUsersToday: number;
  readonly highImpactToday: number;
  readonly accessChangesToday: number;
  readonly timeZone: string;
}

export interface AuditCode {
  readonly code: string;
  readonly label: string;
}

export interface AuditFilterOptions {
  readonly actions: readonly AuditCode[];
  readonly entityTypes: readonly AuditCode[];
  readonly users: readonly { readonly id: string; readonly name: string }[];
  readonly branches: readonly {
    readonly id: string;
    readonly name: string;
    readonly isActive: boolean;
  }[];
}

export interface AuditChange {
  readonly field: string;
  readonly label: string;
  readonly before: string;
  readonly after: string;
}

export interface AuditTarget {
  readonly kind: string;
  readonly id: string;
}

export interface AuditDetail extends Omit<AuditItem, 'actor'> {
  readonly actor: {
    readonly id: string | null;
    readonly name: string;
    readonly roleName: string | null;
  };
  readonly changes: readonly AuditChange[];
  readonly metadata: readonly { readonly key: string; readonly value: string }[];
  readonly target: AuditTarget | null;
  readonly timeZone: string;
}

/** The URL-held view (BR-20); `null` is the default and is omitted from the URL. */
export interface AuditView {
  readonly from: string | null;
  readonly to: string | null;
  readonly q: string;
  readonly actor: string | null;
  readonly action: string | null;
  readonly entity: string | null;
  readonly entityId: string | null;
  readonly branch: string | null;
  readonly impact: AuditImpact | null;
  readonly sort: AuditSort;
  readonly direction: AuditDirection;
  readonly page: number;
  readonly pageSize: number;
}

/** Filters and sort sent to `/audit-log` and `/audit-log/export`. */
export type AuditQuery = Pick<
  AuditView,
  | 'from'
  | 'to'
  | 'q'
  | 'actor'
  | 'action'
  | 'entity'
  | 'entityId'
  | 'branch'
  | 'impact'
  | 'sort'
  | 'direction'
>;

export interface AuditFile {
  readonly blob: Blob;
  readonly fileName: string;
}
