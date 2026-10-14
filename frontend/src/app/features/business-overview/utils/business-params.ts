import { addDays, isValidDate, todayInZone } from '../../schedule/utils/schedule-range';
import { BusinessOptions, BusinessRoute, RangePreset } from '../models/business-overview.model';

/** BR-03: a period is at most 366 days long. */
export const MAX_PERIOD_DAYS = 366;

export const DEFAULT_RANGE = 'this_month' as const;

export const RANGE_CHOICES: readonly { readonly id: RangePreset; readonly name: string }[] = [
  { id: 'this_month', name: 'This month' },
  { id: 'last_month', name: 'Last month' },
  { id: 'last_30_days', name: 'Last 30 days' },
  { id: 'this_quarter', name: 'This quarter' },
  { id: 'this_year', name: 'This year' },
  { id: 'custom', name: 'Custom' },
];

export interface ResolvedBusinessRoute {
  readonly route: BusinessRoute;
  /** Canonical query values; `null` removes the parameter. */
  readonly query: Readonly<Record<string, string | null>>;
  /** True when the URL differs from the canonical values and must be rewritten (replaceUrl). */
  readonly normalized: boolean;
}

export interface QueryValues {
  get(name: string): string | null;
}

const MS_PER_DAY = 86_400_000;

/** Inclusive number of days of a period. */
export function periodLength(from: string, to: string): number {
  return (
    Math.round((Date.parse(`${to}T00:00:00Z`) - Date.parse(`${from}T00:00:00Z`)) / MS_PER_DAY) + 1
  );
}

/** BR-03: `from`/`to` of a preset relative to the local `today`. */
export function presetPeriod(
  preset: Exclude<RangePreset, 'custom'>,
  today: string,
): { readonly from: string; readonly to: string } {
  const monthStart = `${today.slice(0, 7)}-01`;
  switch (preset) {
    case 'this_month':
      return { from: monthStart, to: today };
    case 'last_month': {
      const to = addDays(monthStart, -1);
      return { from: `${to.slice(0, 7)}-01`, to };
    }
    case 'last_30_days':
      return { from: addDays(today, -29), to: today };
    case 'this_quarter': {
      const quarterMonth = Math.floor((Number(today.slice(5, 7)) - 1) / 3) * 3 + 1;
      return { from: `${today.slice(0, 5)}${String(quarterMonth).padStart(2, '0')}-01`, to: today };
    }
    case 'this_year':
      return { from: `${today.slice(0, 4)}-01-01`, to: today };
  }
}

function isPreset(value: string | null): value is Exclude<RangePreset, 'custom'> {
  return RANGE_CHOICES.some((choice) => choice.id === value && value !== 'custom');
}

function validCustom(from: string | null, to: string | null, today: string): boolean {
  return (
    isValidDate(from) &&
    isValidDate(to) &&
    from <= to &&
    to <= today &&
    periodLength(from, to) <= MAX_PERIOD_DAYS
  );
}

/**
 * BR-03/BR-04: resolves the URL into a valid selection. An unknown preset, an invalid or incomplete
 * custom range and an unknown branch fall back to their defaults; `compare` is `false` only when
 * the URL says so.
 */
export function resolveBusinessRoute(
  params: QueryValues,
  options: BusinessOptions,
  now: Date = new Date(),
): ResolvedBusinessRoute {
  const today = todayInZone(options.timezone, now);
  const rangeParam = params.get('range');
  const fromParam = params.get('from');
  const toParam = params.get('to');

  let range: RangePreset = DEFAULT_RANGE;
  let period = presetPeriod(DEFAULT_RANGE, today);
  let rangeQuery: string | null = null;
  if (isPreset(rangeParam)) {
    range = rangeParam;
    period = presetPeriod(rangeParam, today);
    rangeQuery = rangeParam;
  } else if (rangeParam === 'custom' && validCustom(fromParam, toParam, today)) {
    range = 'custom';
    period = { from: fromParam as string, to: toParam as string };
    rangeQuery = 'custom';
  }

  const requested = params.get('branch');
  let branchId: string | null =
    requested !== null && options.branches.some((branch) => branch.id === requested)
      ? requested
      : null;
  if (branchId === null && options.branches.length === 1) {
    branchId = options.branches[0].id;
  }

  const compare = params.get('compare') !== 'false';
  const query = {
    range: rangeQuery,
    from: range === 'custom' ? period.from : null,
    to: range === 'custom' ? period.to : null,
    branch: branchId,
    compare: compare ? null : 'false',
  };
  const normalized = Object.entries(query).some(([key, value]) => params.get(key) !== value);
  return { route: { range, ...period, branchId, compare }, query, normalized };
}

/** BR-03 subtitle: "Sep 1 – 20, 2026" or "Aug 15, 2026 – Sep 20, 2026", then the branch. */
export function periodLabel(from: string, to: string): string {
  const months = [
    'Jan',
    'Feb',
    'Mar',
    'Apr',
    'May',
    'Jun',
    'Jul',
    'Aug',
    'Sep',
    'Oct',
    'Nov',
    'Dec',
  ];
  const part = (date: string): { m: string; d: number; y: string } => ({
    m: months[Number(date.slice(5, 7)) - 1],
    d: Number(date.slice(8, 10)),
    y: date.slice(0, 4),
  });
  const a = part(from);
  const b = part(to);
  if (from === to) {
    return `${a.m} ${a.d}, ${a.y}`;
  }
  return a.m === b.m && a.y === b.y
    ? `${a.m} ${a.d} – ${b.d}, ${a.y}`
    : `${a.m} ${a.d}, ${a.y} – ${b.m} ${b.d}, ${b.y}`;
}
