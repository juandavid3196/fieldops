import { RangePreset } from '../../business-overview/models/business-overview.model';

import { t } from '../../../core/i18n/translate';
/** Reports workspace contracts (Final: specs/reports-workspace/spec.md, API contracts). */

export type { RangePreset };

export type ReportKey =
  | 'job_profitability'
  | 'invoice_aging'
  | 'quote_conversion'
  | 'jobs_on_time'
  | 'technician_utilization';

export type SortDirection = 'asc' | 'desc';
export type ReportFormat = 'csv' | 'pdf';
export type MetricUnit = 'currency' | 'count' | 'percent' | 'hours' | 'minutes';
export type ChartKind =
  'category_bars' | 'bucket_bars' | 'grouped_series' | 'stacked_series' | 'technician_bars';

/** BR-05 report filters; an absent key means `all`. */
export type FilterKey = 'status' | 'category' | 'technician' | 'bucket' | 'event' | 'onTime';
export type ReportFilterValues = Readonly<Partial<Record<FilterKey, string>>>;

export const REPORT_PAGE_SIZES: readonly number[] = [10, 25, 50];
export const DEFAULT_PAGE_SIZE = 10;

export const REPORTS_FORBIDDEN_MESSAGE = (): string =>
  t('reports.reportsModel.reportsForbiddenMessage');
export const REPORTS_NO_BRANCHES_MESSAGE = (): string =>
  t('reports.reportsModel.reportsNoBranchesMessage');
export const REPORTS_LOAD_ERROR_MESSAGE = (): string =>
  t('reports.reportsModel.reportsLoadErrorMessage');
export const REPORTS_REFRESH_ERROR_MESSAGE = (): string =>
  t('reports.reportsModel.reportsRefreshErrorMessage');
export const REPORTS_EXPORT_FAILED_MESSAGE = (): string =>
  t('reports.reportsModel.reportsExportFailedMessage');
export const REPORTS_CSV_TOO_LARGE_MESSAGE = (): string =>
  t('reports.reportsModel.reportsCsvTooLargeMessage');
export const REPORTS_PDF_TOO_LARGE_MESSAGE = (): string =>
  t('reports.reportsModel.reportsPdfTooLargeMessage');
export const REPORTS_HISTORY_ERROR_MESSAGE = (): string =>
  t('reports.reportsModel.reportsHistoryErrorMessage');
export const REPORTS_HISTORY_EMPTY_MESSAGE = (): string =>
  t('reports.reportsModel.reportsHistoryEmptyMessage');

export interface ReportInfo {
  readonly key: ReportKey;
  readonly group: string;
  readonly name: string;
  readonly description: string;
}

export interface ReportChoice {
  readonly id: string;
  readonly name: string;
  readonly isActive: boolean;
}

export interface ReportTechnician extends ReportChoice {
  readonly branchId: string;
}

/** `GET /reports/options`: reports, branches and technicians are already limited to the caller. */
export interface ReportOptions {
  readonly timezone: string;
  readonly currency: string;
  readonly today: string;
  readonly marginTarget: number;
  readonly reports: readonly ReportInfo[];
  readonly branches: readonly ReportChoice[];
  readonly categories: readonly ReportChoice[];
  readonly technicians: readonly ReportTechnician[];
}

/** The applied state kept in the URL (BR-05). `compare` never reaches the data request. */
export interface ReportsRoute {
  readonly report: ReportKey;
  readonly range: RangePreset;
  readonly from: string;
  readonly to: string;
  readonly branchId: string | null;
  readonly compare: boolean;
  readonly filters: ReportFilterValues;
  readonly sort: string;
  readonly direction: SortDirection;
  readonly page: number;
  readonly pageSize: number;
}

export interface Metric {
  readonly key: string;
  readonly label: string;
  readonly unit: MetricUnit;
  readonly value: number | null;
  readonly previous: number | null;
}

export interface ChartPoint {
  readonly label: string;
  readonly start: string | null;
  readonly values: Readonly<Record<string, number>>;
}

export interface ReportChart {
  readonly kind: ChartKind;
  readonly series: readonly { readonly key: string; readonly label: string }[];
  readonly points: readonly ChartPoint[];
}

/** BR-13: ids of the linked entities, `null` when the caller may not open them. */
export interface ReportLinks {
  readonly job?: string | null;
  readonly visit?: string | null;
  readonly customer?: string | null;
  readonly invoice?: string | null;
  readonly quote?: string | null;
  readonly request?: string | null;
  readonly technician?: string | null;
}

export type LinkKey = keyof ReportLinks;

/** One row: the columns of the report (keyed like its sort allowlist) plus `id` and `links`. */
export interface ReportRow {
  readonly id: string;
  readonly links: ReportLinks;
  readonly [column: string]: unknown;
}

export interface ReportData {
  readonly reportKey: ReportKey;
  readonly from: string | null;
  readonly to: string | null;
  readonly previousFrom: string | null;
  readonly previousTo: string | null;
  readonly asOf: string;
  readonly timezone: string;
  readonly currency: string;
  readonly granularity: 'day' | 'week' | 'month' | null;
  readonly marginTarget: number;
  readonly summary: readonly Metric[];
  readonly notes: { readonly otherCurrencyCount: number | null };
  readonly chart: ReportChart;
  readonly items: readonly ReportRow[];
  readonly totals: Readonly<Record<string, number | string | null>>;
  readonly total: number;
  readonly page: number;
  readonly pageSize: number;
}

/** Normalized applied filters stored with an export (BR-16); names mirror the data query. */
export interface ExportFilters {
  readonly from?: string | null;
  readonly to?: string | null;
  readonly asOf?: string | null;
  readonly branchId?: string | null;
  readonly status?: string | null;
  readonly categoryId?: string | null;
  readonly technicianId?: string | null;
  readonly bucket?: string | null;
  readonly event?: string | null;
  readonly onTime?: string | null;
  readonly sort?: string | null;
  readonly direction?: string | null;
  readonly compare?: boolean | null;
}

export interface ReportExportItem {
  readonly id: number;
  readonly occurredAt: string;
  readonly reportKey: ReportKey;
  readonly reportName: string;
  readonly format: ReportFormat;
  readonly rowCount: number;
  readonly filters: ExportFilters;
}

export interface ReportFile {
  readonly blob: Blob;
  readonly fileName: string;
}
