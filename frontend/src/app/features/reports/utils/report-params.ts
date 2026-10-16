import {
  QueryValues,
  periodLength,
  resolveBusinessRoute,
} from '../../business-overview/utils/business-params';
import { addDays } from '../../schedule/utils/schedule-range';
import {
  DEFAULT_PAGE_SIZE,
  ExportFilters,
  FilterKey,
  REPORT_PAGE_SIZES,
  ReportFilterValues,
  ReportKey,
  ReportOptions,
  ReportsRoute,
  SortDirection,
} from '../models/reports.model';
import {
  FILTER_API_NAMES,
  FILTER_KEYS,
  FilterSpec,
  ReportMeta,
  isReportKey,
  reportMeta,
  sortKeys,
} from './report-catalog';

export interface ResolvedReportsRoute {
  readonly route: ReportsRoute;
  /** Canonical query values; `null` removes the parameter. */
  readonly query: Readonly<Record<string, string | null>>;
  /** True when the URL differs from the canonical values and must be rewritten (replaceUrl). */
  readonly normalized: boolean;
}

/** Every URL parameter owned by this page. */
export const ROUTE_PARAM_NAMES: readonly string[] = [
  'report',
  'range',
  'from',
  'to',
  'branch',
  'compare',
  ...FILTER_KEYS,
  'sort',
  'direction',
  'page',
  'pageSize',
];

/** Parameters cleared when the report changes (BR-05): filters, sort and page. */
export const REPORT_SCOPED_PARAMS: Readonly<Record<string, null>> = Object.fromEntries(
  [...FILTER_KEYS, 'sort', 'direction', 'page'].map((name) => [name, null]),
);

/** Allowed codes of a filter for the options at hand. */
export function filterCodes(spec: FilterSpec, options: ReportOptions): readonly string[] {
  if (spec.source === 'categories') {
    return options.categories.map((item) => item.id);
  }
  if (spec.source === 'technicians') {
    return options.technicians.map((item) => item.id);
  }
  return (spec.choices ?? []).map((choice) => choice.code);
}

function resolveFilters(
  params: QueryValues,
  meta: ReportMeta,
  options: ReportOptions,
): ReportFilterValues {
  const filters: Partial<Record<FilterKey, string>> = {};
  for (const spec of meta.filters) {
    const value = params.get(spec.key);
    if (value !== null && filterCodes(spec, options).includes(value)) {
      filters[spec.key] = value;
    }
  }
  return filters;
}

function resolveSort(
  params: QueryValues,
  meta: ReportMeta,
): { sort: string; direction: SortDirection } {
  const requested = params.get('sort');
  const sort =
    requested !== null && sortKeys(meta).includes(requested) ? requested : meta.defaultSort;
  const requestedDirection = params.get('direction');
  const direction: SortDirection =
    requestedDirection === 'asc' || requestedDirection === 'desc'
      ? requestedDirection
      : sort === meta.defaultSort
        ? meta.defaultDirection
        : 'asc';
  return { sort, direction };
}

function positiveInt(value: string | null): number | null {
  return value !== null && /^[1-9]\d{0,5}$/.test(value) ? Number(value) : null;
}

/**
 * BR-04/BR-05: resolves the URL into a valid selection. A report not visible to the caller falls
 * back to the first visible one; an unknown or not-applicable value falls back to its default.
 * `options.reports` must not be empty.
 */
export function resolveReportsRoute(
  params: QueryValues,
  options: ReportOptions,
  now: Date = new Date(),
): ResolvedReportsRoute {
  const requested = params.get('report');
  const report: ReportKey =
    isReportKey(requested) && options.reports.some((item) => item.key === requested)
      ? requested
      : options.reports[0].key;
  const meta = reportMeta(report);
  const base = resolveBusinessRoute(params, options, now);
  const filters = resolveFilters(params, meta, options);
  const { sort, direction } = resolveSort(params, meta);
  const size = positiveInt(params.get('pageSize'));
  const pageSize = size !== null && REPORT_PAGE_SIZES.includes(size) ? size : DEFAULT_PAGE_SIZE;
  const page = positiveInt(params.get('page')) ?? 1;

  const route: ReportsRoute = { report, ...base.route, filters, sort, direction, page, pageSize };
  const query = routeParams(route);
  const normalized = ROUTE_PARAM_NAMES.some((name) => params.get(name) !== (query[name] ?? null));
  return { route, query, normalized };
}

/** The canonical URL parameters of a state: defaults are omitted (BR-05). */
export function routeParams(route: ReportsRoute): Record<string, string | null> {
  const meta = reportMeta(route.report);
  const customSort = route.sort !== meta.defaultSort || route.direction !== meta.defaultDirection;
  const custom = route.range === 'custom';
  const params: Record<string, string | null> = {
    report: route.report,
    range: route.range === 'this_month' ? null : route.range,
    from: custom ? route.from : null,
    to: custom ? route.to : null,
    branch: route.branchId,
    compare: route.compare ? null : 'false',
    sort: customSort ? route.sort : null,
    direction: customSort ? route.direction : null,
    page: route.page > 1 ? String(route.page) : null,
    pageSize: route.pageSize === DEFAULT_PAGE_SIZE ? null : String(route.pageSize),
  };
  for (const key of FILTER_KEYS) {
    const value = route.filters[key];
    params[key] =
      value !== undefined && meta.filters.some((spec) => spec.key === key) ? value : null;
  }
  return params;
}

/** The data request query (BR-07); `compare` and `range` never reach it. */
export function reportQueryParams(route: ReportsRoute): Record<string, string> {
  const params: Record<string, string> = {
    from: route.from,
    to: route.to,
    sort: route.sort,
    direction: route.direction,
    page: String(route.page),
    pageSize: String(route.pageSize),
  };
  if (route.branchId !== null) {
    params['branchId'] = route.branchId;
  }
  for (const key of FILTER_KEYS) {
    const value = route.filters[key];
    if (value !== undefined) {
      params[FILTER_API_NAMES[key]] = value;
    }
  }
  return params;
}

/** The export query: the data query without paging, plus `format` and `compare` (BR-14). */
export function exportQueryParams(
  route: ReportsRoute,
  format: 'csv' | 'pdf',
): Record<string, string> {
  const params = reportQueryParams(route);
  delete params['page'];
  delete params['pageSize'];
  return { ...params, format, compare: String(route.compare) };
}

/** BR-04: the previous period has the same length and ends the day before `from`. */
export function previousPeriod(from: string, to: string): { from: string; to: string } {
  const end = addDays(from, -1);
  return { from: addDays(end, -(periodLength(from, to) - 1)), to: end };
}

/** BR-17: "Open report" restores the export's report, period (as custom), branch, filters and sort. */
export function routeFromExport(
  reportKey: ReportKey,
  filters: ExportFilters,
): Record<string, string | null> {
  const meta = reportMeta(reportKey);
  const hasPeriod = !meta.asOfToday && !!filters.from && !!filters.to;
  const params: Record<string, string | null> = {
    report: reportKey,
    range: hasPeriod ? 'custom' : null,
    from: hasPeriod ? (filters.from ?? null) : null,
    to: hasPeriod ? (filters.to ?? null) : null,
    branch: filters.branchId ?? null,
    compare: filters.compare === false ? 'false' : null,
    sort: filters.sort ?? null,
    direction: filters.direction ?? null,
    page: null,
    pageSize: null,
    status: filters.status ?? null,
    category: filters.categoryId ?? null,
    technician: filters.technicianId ?? null,
    bucket: filters.bucket ?? null,
    event: filters.event ?? null,
    onTime: filters.onTime ?? null,
  };
  return params;
}
