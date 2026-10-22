import { Delta, EMPTY_VALUE, delta, money } from '../../business-overview/utils/business-view';
import { formatMinutes, zoned } from '../../schedule/utils/schedule-range';
import { LinkKey, Metric, ReportChart, ReportRow } from '../models/reports.model';
import { ChartUnit, ReportColumn } from './report-catalog';

import { localizedList, t } from '../../../core/i18n/translate';
import { activeIntlLocale } from '../../../core/i18n/active-locale';
export const BELOW_TARGET_TEXT = (): string => t('reports.reportView.belowTargetText');

const MONTHS = localizedList(() => [
  t('reports.reportView.jan'),
  t('reports.reportView.feb'),
  t('reports.reportView.mar'),
  t('reports.reportView.apr'),
  t('reports.reportView.may'),
  t('reports.reportView.jun'),
  t('reports.reportView.jul'),
  t('reports.reportView.aug'),
  t('reports.reportView.sep'),
  t('reports.reportView.oct'),
  t('reports.reportView.nov'),
  t('reports.reportView.dec'),
]);

/** BR-04: "MMM d, yyyy" of a local `YYYY-MM-DD` date. */
export function formatDate(date: string): string {
  return `${MONTHS[Number(date.slice(5, 7)) - 1]} ${Number(date.slice(8, 10))}, ${date.slice(0, 4)}`;
}

/** BR-04: the organization-local date of an instant. */
export function formatInstantDate(iso: string, timezone: string): string {
  return formatDate(zoned(iso, timezone).date);
}

/** BR-04: "MMM d, yyyy h:mm a" in the organization time zone. */
export function formatDateTime(iso: string, timezone: string): string {
  const local = zoned(iso, timezone);
  return `${formatDate(local.date)} ${formatMinutes(local.minutes)}`;
}

/** Table money keeps cents (BR-14 precision); cards use whole units. */
export function moneyExact(value: number, currency: string): string {
  return new Intl.NumberFormat(activeIntlLocale(), { style: 'currency', currency }).format(value);
}

/** `awaiting_response` → "Awaiting response"; text that already reads as a label is kept. */
export function humanize(value: string): string {
  if (!/^[a-z0-9_]+$/.test(value)) {
    return value;
  }
  const text = value.replaceAll('_', ' ');
  return `${text.charAt(0).toUpperCase()}${text.slice(1)}`;
}

/** BR-06 minute metrics and the Variance column: "+5 min", "−5 min", "On estimate". */
export function varianceText(minutes: number | null): string {
  if (minutes === null) {
    return EMPTY_VALUE;
  }
  if (minutes === 0) {
    return t('reports.reportView.onEstimate');
  }
  return minutes > 0
    ? t('reports.reportView.min', { minutes: minutes })
    : t('reports.reportView.min2', { minutes: Math.abs(minutes) });
}

export interface LinkTarget {
  readonly commands: readonly string[];
  readonly queryParams: Readonly<Record<string, string>> | null;
}

/** BR-13: destinations; each one keeps its own authorization. */
export function linkTarget(key: LinkKey, id: string): LinkTarget {
  switch (key) {
    case 'job':
    case 'visit':
      return { commands: ['/jobs', id], queryParams: null };
    case 'customer':
      return { commands: ['/customers', id], queryParams: null };
    case 'invoice':
      return { commands: ['/invoices', id], queryParams: null };
    case 'quote':
      return { commands: ['/quotes', id], queryParams: null };
    case 'request':
      return { commands: ['/requests'], queryParams: { request: id } };
    case 'technician':
      return { commands: ['/team', id, 'skills-availability'], queryParams: null };
  }
}

export interface CellContext {
  readonly timezone: string;
  readonly currency: string;
  readonly marginTarget: number;
}

export type TagTone = 'neutral' | 'ok' | 'warn' | 'danger' | 'info';

export interface CellTag {
  readonly tone: TagTone;
  readonly icon: string;
}

export interface CellView {
  readonly text: string;
  readonly link: LinkTarget | null;
  readonly mark: 'below-target' | 'invoiced' | 'not-invoiced' | null;
  /** Status, bucket and on-time cells render as a pill with an icon (never color alone). */
  readonly tag?: CellTag | null;
}

const TAGS: Readonly<Record<string, CellTag>> = {
  on_time: { tone: 'ok', icon: 'pi-check-circle' },
  late: { tone: 'warn', icon: 'pi-clock' },
  not_started: { tone: 'danger', icon: 'pi-times-circle' },
  not_due: { tone: 'neutral', icon: 'pi-calendar' },
  scheduled: { tone: 'neutral', icon: 'pi-calendar' },
  active: { tone: 'info', icon: 'pi-play-circle' },
  completed: { tone: 'ok', icon: 'pi-check-circle' },
  current: { tone: 'ok', icon: 'pi-check-circle' },
  '1_30': { tone: 'warn', icon: 'pi-clock' },
  '31_60': { tone: 'warn', icon: 'pi-exclamation-triangle' },
  '60_plus': { tone: 'danger', icon: 'pi-exclamation-circle' },
  sent: { tone: 'info', icon: 'pi-send' },
  partially_paid: { tone: 'warn', icon: 'pi-percentage' },
  overdue: { tone: 'danger', icon: 'pi-exclamation-circle' },
  draft: { tone: 'neutral', icon: 'pi-pencil' },
  awaiting_response: { tone: 'info', icon: 'pi-send' },
  clarification_requested: { tone: 'warn', icon: 'pi-question-circle' },
  expired: { tone: 'warn', icon: 'pi-clock' },
  approved: { tone: 'ok', icon: 'pi-check-circle' },
  rejected: { tone: 'danger', icon: 'pi-times-circle' },
  cancelled: { tone: 'neutral', icon: 'pi-ban' },
};

const TAG_COLUMNS = new Set(['status', 'bucket', 'onTime']);

function tagFor(column: ReportColumn, row: ReportRow, text: string | null): CellTag | null {
  const code = row[column.valueKey ?? column.key];
  return text !== null &&
    TAG_COLUMNS.has(column.key) &&
    column.type === 'label' &&
    typeof code === 'string'
    ? (TAGS[code] ?? { tone: 'neutral', icon: 'pi-info-circle' })
    : null;
}

const num = (value: unknown): number | null => (typeof value === 'number' ? value : null);

function formatValue(
  type: ReportColumn['type'],
  value: unknown,
  ctx: CellContext,
  currency: string,
): string | null {
  const n = num(value);
  switch (type) {
    case 'text':
      return typeof value === 'string' && value !== '' ? value : null;
    case 'label':
      return typeof value === 'string' && value !== '' ? humanize(value) : null;
    case 'date':
      return typeof value === 'string' ? formatDate(value) : null;
    case 'instantDate':
      return typeof value === 'string' ? formatInstantDate(value, ctx.timezone) : null;
    case 'instant':
      return typeof value === 'string' ? formatDateTime(value, ctx.timezone) : null;
    case 'money':
      return n === null ? null : moneyExact(n, currency);
    case 'percentOne':
      return n === null ? null : `${n.toFixed(1)}%`;
    case 'percentWhole':
      return n === null ? null : `${Math.round(n)}%`;
    case 'count':
      return n === null ? null : String(n);
    case 'hours':
      return n === null ? null : `${Math.round(n / 60)}h`;
    case 'minutes':
      return n === null ? null : t('reports.reportView.min3', { n: n });
    case 'variance':
      return n === null ? null : varianceText(n);
    case 'invoicing':
      return null;
  }
}

function isBelowTarget(
  column: ReportColumn,
  value: unknown,
  ctx: CellContext,
  flag?: unknown,
): boolean {
  const n = num(value);
  return (
    column.key === 'margin' &&
    (flag === true || (flag === undefined && n !== null && n < ctx.marginTarget))
  );
}

/** One table cell (BR-08 … BR-12); `null` values show "—" or the column fallback. */
export function cellView(column: ReportColumn, row: ReportRow, ctx: CellContext): CellView {
  const value = row[column.labelKey ?? column.valueKey ?? column.key];
  if (column.type === 'invoicing') {
    const invoiced = value === true;
    return {
      text: invoiced ? t('reports.reportView.invoiced') : t('reports.reportView.notInvoiced'),
      link: null,
      mark: invoiced ? 'invoiced' : 'not-invoiced',
    };
  }
  const currency = typeof row['currency'] === 'string' ? row['currency'] : ctx.currency;
  const text = formatValue(column.type, value, ctx, currency);
  const id = column.link === undefined ? null : (row.links[column.link] ?? null);
  return {
    text: text ?? column.fallback ?? EMPTY_VALUE,
    link:
      text !== null && column.link !== undefined && id !== null
        ? linkTarget(column.link, id)
        : null,
    mark: isBelowTarget(column, value, ctx, row['belowTarget']) ? 'below-target' : null,
    tag: tagFor(column, row, text),
  };
}

/** The totals cell of a column, or `null` when the table shows none (BR-07). */
export function totalView(
  column: ReportColumn,
  totals: Readonly<Record<string, number | string | null>>,
  ctx: CellContext,
): CellView | null {
  const value = totals[column.totalKey ?? column.key];
  if (value === undefined || value === null) {
    return null;
  }
  const text = formatValue(column.totalType ?? column.type, value, ctx, ctx.currency);
  return text === null
    ? null
    : {
        text: `${text}${column.totalUnit ?? ''}`,
        link: null,
        mark: isBelowTarget(column, value, ctx) ? 'below-target' : null,
      };
}

export interface SummaryCard {
  readonly key: string;
  readonly label: string;
  readonly value: string;
  /** BR-06 relative change of count, money and hour metrics. */
  readonly delta: Delta | null;
  readonly badWhenUp: boolean;
  /** BR-06 "vs … previous" text of percent and minute metrics. */
  readonly previousText: string | null;
  readonly caption: string | null;
}

export interface SummaryContext {
  readonly currency: string;
  /** Comparison on and applicable (never for invoice aging). */
  readonly compare: boolean;
  readonly percentDecimals: 0 | 1;
}

const JOBS_COMPLETED_CAPTION = (): string => t('reports.reportView.jobsCompletedCaption');

function metricValue(metric: Metric, value: number | null, ctx: SummaryContext): string {
  if (value === null) {
    return EMPTY_VALUE;
  }
  switch (metric.unit) {
    case 'currency':
      return money(value, ctx.currency);
    case 'percent':
      return `${value.toFixed(ctx.percentDecimals)}%`;
    case 'hours':
      return `${Math.round(value)}h`;
    case 'minutes':
      return varianceText(value);
    case 'count':
      return String(value);
  }
}

/** BR-06: the cards of the summary with their optional previous-period comparison. */
export function summaryCards(metrics: readonly Metric[], ctx: SummaryContext): SummaryCard[] {
  return metrics.map((metric) => {
    const percentLike = metric.unit === 'percent' || metric.unit === 'minutes';
    const relative =
      ctx.compare && !percentLike && metric.value !== null && metric.previous !== null
        ? delta(metric.value, metric.previous)
        : null;
    return {
      key: metric.key,
      label: metric.label,
      value: metricValue(metric, metric.value, ctx),
      delta: relative,
      badWhenUp: metric.key === 'overtimeHours',
      previousText:
        ctx.compare && percentLike
          ? t('reports.reportView.vsPrevious', { value: metricValue(metric, metric.previous, ctx) })
          : null,
      caption: metric.key === 'jobsCompleted' ? JOBS_COMPLETED_CAPTION() : null,
    };
  });
}

export interface BarItem {
  readonly key: string;
  readonly label: string;
  readonly value: number;
  readonly text: string;
  /** Share of the longest bar, 0–100. */
  readonly percent: number;
  /** Palette index: 0 primary, 1 secondary, 2 tertiary. */
  readonly tone: number;
}

export interface BarGroup {
  readonly label: string;
  readonly bars: readonly BarItem[];
}

export interface BarChart {
  readonly horizontal: boolean;
  readonly stacked: boolean;
  readonly empty: boolean;
  readonly legend: readonly { readonly label: string; readonly tone: number }[];
  readonly groups: readonly BarGroup[];
}

function barText(value: number, unit: ChartUnit, currency: string): string {
  return unit === 'money'
    ? money(value, currency)
    : unit === 'percent'
      ? `${Math.round(value)}%`
      : String(value);
}

/** Pure geometry of the grouped, stacked and horizontal bar charts (BR-08 … BR-12). */
export function barChart(chart: ReportChart, unit: ChartUnit, currency: string): BarChart {
  const keys =
    chart.series.length > 0
      ? chart.series.map((series) => series.key)
      : Object.keys(chart.points[0]?.values ?? {});
  const stacked = chart.kind === 'stacked_series';
  const values = chart.points.map((point) =>
    keys.map((key) => Math.max(0, point.values[key] ?? 0)),
  );
  const longest = Math.max(
    unit === 'percent' ? 100 : 0,
    ...values.map((group) =>
      stacked ? group.reduce((sum, value) => sum + value, 0) : Math.max(0, ...group),
    ),
  );
  const percent = (value: number): number =>
    longest === 0 || value === 0 ? 0 : Math.max(1, (value / longest) * 100);
  const groups = chart.points.map((point, index): BarGroup => ({
    label: point.label,
    bars: keys.map((key, position): BarItem => {
      const value = values[index][position];
      return {
        key,
        label: chart.series.find((series) => series.key === key)?.label ?? key,
        value,
        text: barText(value, unit, currency),
        percent: percent(value),
        tone: position,
      };
    }),
  }));
  return {
    horizontal: chart.kind === 'category_bars' || chart.kind === 'technician_bars',
    stacked,
    empty: values.every((group) => group.every((value) => value === 0)),
    legend:
      chart.series.length > 1
        ? chart.series.map((series, tone) => ({ label: series.label, tone }))
        : [],
    groups,
  };
}
