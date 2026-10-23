export type JobHistoryStatus = 'all' | 'completed' | 'reassigned' | 'cancelled';
export type JobHistoryPeriod = '30d' | '90d' | '12m' | 'all';
export type JobHistorySort = 'newest' | 'oldest';
export type JobHistoryOutcome = 'completed' | 'reassigned' | 'cancelled';

export const JOB_HISTORY_STATUSES: readonly JobHistoryStatus[] = [
  'all',
  'completed',
  'reassigned',
  'cancelled',
];
export const JOB_HISTORY_PERIODS: readonly JobHistoryPeriod[] = ['30d', '90d', '12m', 'all'];
export const JOB_HISTORY_SORTS: readonly JobHistorySort[] = ['newest', 'oldest'];
export const JOB_HISTORY_PAGE_SIZES: readonly number[] = [10, 25, 50];

export interface JobHistoryQuery {
  readonly search: string;
  readonly status: JobHistoryStatus;
  readonly period: JobHistoryPeriod;
  readonly sort: JobHistorySort;
  readonly page: number;
  readonly pageSize: number;
}

export interface JobHistoryItem {
  readonly visitId: string;
  readonly workOrderId: string;
  readonly displayNumber: string;
  readonly title: string;
  readonly visitNumber: number;
  readonly visitCount: number;
  readonly property: { readonly name: string; readonly city: string };
  /** Local `yyyy-MM-dd`. */
  readonly participationDate: string;
  readonly loggedMinutes: number;
  readonly outcome: JobHistoryOutcome;
  readonly canOpen: boolean;
}

export interface JobHistorySummary {
  readonly completedJobs: number;
  readonly onTimePercent?: number | null;
  readonly loggedMinutes: number;
  readonly avgDurationMinutes?: number | null;
}

export interface JobHistoryResponse {
  readonly timezone: string;
  readonly period: { readonly from?: string | null; readonly to: string };
  readonly summary: JobHistorySummary;
  readonly items: readonly JobHistoryItem[];
  readonly total: number;
  readonly page: number;
  readonly pageSize: number;
}
