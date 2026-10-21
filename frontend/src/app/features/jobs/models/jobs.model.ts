import { Priority } from './work-order.model';

import { t } from '../../../core/i18n/translate';
/** Fixed copy of the jobs workspace (specs/jobs-management/spec.md UI states; backend text is never shown). */
export const JOBS_LOAD_ERROR_MESSAGE = (): string => t('jobs.jobsModel.jobsLoadErrorMessage');
export const JOBS_METRICS_ERROR_MESSAGE = (): string => t('jobs.jobsModel.jobsMetricsErrorMessage');
export const JOBS_EMPTY_MESSAGE = (): string => t('jobs.jobsModel.jobsEmptyMessage');
export const JOBS_FILTERED_EMPTY_MESSAGE = (): string =>
  t('jobs.jobsModel.jobsFilteredEmptyMessage');
export const JOBS_NO_BRANCHES_MESSAGE = (): string => t('jobs.jobsModel.jobsNoBranchesMessage');

/** BR-10: rows per page offered by the UI (the first is the default). */
export const JOBS_PAGE_SIZES: readonly number[] = [10, 25, 50];
/** BR-16: URL parameters owned by the workspace. */
export const JOBS_QUERY_KEYS = [
  'tab',
  'q',
  'branch',
  'status',
  'priority',
  'sort',
  'page',
  'pageSize',
] as const;

export type JobsTab = 'all' | 'active' | 'unscheduled' | 'completed';
export type JobsSort = 'newest' | 'oldest' | 'updated' | 'schedule' | 'priority';
export type JobsPriority = Priority;

/** BR-05 operational status codes (the API returns the code; the frontend owns label and tone). */
export type JobStatusCode =
  | 'ready_to_schedule'
  | 'scheduled'
  | 'assigned'
  | 'on_the_way'
  | 'in_progress'
  | 'paused'
  | 'completed'
  | 'needs_correction'
  | 'approved_for_billing'
  | 'cancelled';

export type JobActionCode = 'view' | 'schedule_visit' | 'open_in_schedule' | 'review_completion';

export interface JobsOptions {
  readonly branches: readonly {
    readonly id: string;
    readonly name: string;
    readonly timezone: string | null;
  }[];
  readonly organizationTimezone: string;
  readonly canManage: boolean;
}

export interface JobsSummary {
  readonly timezone: string;
  readonly activeJobs: { readonly total: number; readonly inProgress: number };
  readonly readyToSchedule: number;
  readonly today: {
    readonly total: number;
    readonly assigned: number;
    readonly completed: number;
  };
  readonly needsAttention: number;
}

export interface JobRow {
  readonly id: string;
  readonly displayNumber: string;
  readonly title: string;
  readonly customerName: string;
  readonly property: {
    readonly name: string;
    readonly postalCode: string | null;
    readonly city: string;
  };
  readonly branch: { readonly id: string; readonly name: string };
  readonly timezone: string;
  readonly status: JobStatusCode;
  readonly priority: JobsPriority;
  readonly currentVisit: {
    readonly id: string;
    readonly scheduledStart: string | null;
    readonly scheduledEnd: string | null;
  } | null;
  readonly technician: {
    readonly fullName: string;
    readonly initials: string;
    readonly colorHex: string | null;
  } | null;
  readonly updatedAt: string;
  readonly createdAt: string;
  readonly actions: readonly JobActionCode[];
}

export interface JobsPage {
  readonly items: readonly JobRow[];
  readonly total: number;
  readonly counts: Readonly<Record<JobsTab, number>>;
}

/** Normalized URL state of `/jobs` (BR-16); `branch` is null for the default branch. */
export interface JobsView {
  readonly tab: JobsTab;
  readonly q: string;
  readonly branch: string | null;
  readonly status: JobStatusCode | null;
  readonly priority: JobsPriority | null;
  readonly sort: JobsSort;
  readonly page: number;
  readonly pageSize: number;
}

/** Query of `GET /work-orders` (the effective branch id is resolved by the page). */
export interface JobsRequest {
  readonly tab: JobsTab;
  readonly q: string;
  readonly branchId: string | null;
  readonly status: JobStatusCode | null;
  readonly priority: JobsPriority | null;
  readonly sort: JobsSort;
  readonly page: number;
  readonly pageSize: number;
}
