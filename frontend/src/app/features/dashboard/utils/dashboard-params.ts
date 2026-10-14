import { addDays, isValidDate, todayInZone } from '../../schedule/utils/schedule-range';
import { DashboardOptions, DashboardPeriod, DashboardRoute } from '../models/dashboard.model';

/** BR-04: the date picker accepts today ± 365 days. */
export const DATE_RANGE_DAYS = 365;

export interface ResolvedRoute {
  readonly route: DashboardRoute;
  /** Canonical query values; `null` removes the parameter. */
  readonly query: Readonly<Record<string, string | null>>;
  /** True when the URL differs from the canonical values and must be rewritten (replaceUrl). */
  readonly normalized: boolean;
}

export interface QueryValues {
  get(name: string): string | null;
}

/** BR-03: branch time zone, falling back to the organization zone (also for All branches). */
export function zoneOf(options: DashboardOptions, branchId: string | null): string {
  const branch = options.branches.find((item) => item.id === branchId);
  return branch?.timezone ?? options.organizationTimezone;
}

/**
 * BR-03/BR-04/BR-05/BR-14: resolves the URL into a valid route. Unknown branch, technician or
 * category, an out-of-range date and an invalid period fall back to their defaults.
 */
export function resolveRoute(
  params: QueryValues,
  options: DashboardOptions,
  now: Date = new Date(),
): ResolvedRoute {
  const requested = params.get('branch');
  let branchId: string | null =
    requested !== null && options.branches.some((branch) => branch.id === requested)
      ? requested
      : null;
  if (branchId === null && options.branches.length === 1) {
    branchId = options.branches[0].id;
  }

  const today = todayInZone(zoneOf(options, branchId), now);
  const dateParam = params.get('date');
  const date =
    isValidDate(dateParam) &&
    dateParam >= addDays(today, -DATE_RANGE_DAYS) &&
    dateParam <= addDays(today, DATE_RANGE_DAYS)
      ? dateParam
      : today;

  const technicianParam = params.get('technician');
  const technicianId =
    technicianParam !== null &&
    options.technicians.some(
      (item) => item.id === technicianParam && (branchId === null || item.branchId === branchId),
    )
      ? technicianParam
      : null;

  const categoryParam = params.get('category');
  const categoryId =
    categoryParam !== null && options.categories.some((item) => item.id === categoryParam)
      ? categoryParam
      : null;

  const periodParam = params.get('period');
  const period: DashboardPeriod = periodParam === '30' ? '30' : '7';

  const query = {
    branch: branchId,
    date,
    technician: technicianId,
    category: categoryId,
    period: periodParam === '7' || periodParam === '30' ? periodParam : null,
  };
  const normalized = Object.entries(query).some(([key, value]) => params.get(key) !== value);
  return { route: { branchId, date, technicianId, categoryId, period }, query, normalized };
}

/** BR-05: number of active filters shown on the Filters button. */
export function activeFilterCount(route: DashboardRoute): number {
  return (route.technicianId === null ? 0 : 1) + (route.categoryId === null ? 0 : 1);
}

export interface AppLink {
  readonly path: readonly string[];
  readonly queryParams?: Readonly<Record<string, string>>;
}

/** BR-17: `/schedule?branch=<b>&view=day&date=<date>`, `branch` omitted with All. */
export function boardLink(branchId: string | null, date: string): AppLink {
  return {
    path: ['/schedule'],
    queryParams:
      branchId === null ? { view: 'day', date } : { branch: branchId, view: 'day', date },
  };
}

/** BR-18: the board link plus the visit to open in the dispatch drawer. */
export function visitLink(branchId: string, date: string, visitId: string): AppLink {
  return {
    path: ['/schedule'],
    queryParams: { branch: branchId, view: 'day', date, visit: visitId },
  };
}

export function jobLink(workOrderId: string): AppLink {
  return { path: ['/jobs', workOrderId] };
}

export const JOBS_LINK: AppLink = { path: ['/jobs'] };
export const TEAM_LINK: AppLink = { path: ['/team'] };
