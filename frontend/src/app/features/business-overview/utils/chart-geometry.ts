import { monthDay } from '../../schedule/utils/schedule-range';
import { Granularity, SeriesBucket } from '../models/business-overview.model';

export type ChartTab = 'revenue' | 'profit' | 'cash';

export const CHART_TABS: readonly { readonly id: ChartTab; readonly name: string }[] = [
  { id: 'revenue', name: 'Revenue' },
  { id: 'profit', name: 'Estimated profit' },
  { id: 'cash', name: 'Cash collected' },
];

export const CHART_WIDTH = 640;
export const CHART_HEIGHT = 260;
const LEFT = 56;
const RIGHT = 16;
const TOP = 12;
const BOTTOM = 28;
const MAX_X_LABELS = 7;

export interface ChartLine {
  readonly id: string;
  /** Legend text: "Revenue (current)" / "Revenue (previous)". */
  readonly label: string;
  readonly dashed: boolean;
  /** Index into the series palette: 0 revenue, 1 profit, 2 collected. */
  readonly tone: 0 | 1 | 2;
  readonly values: readonly number[];
  /** SVG `points` attribute. */
  readonly points: string;
}

export interface AxisTick {
  readonly y: number;
  readonly label: string;
}

export interface ChartModel {
  readonly empty: boolean;
  readonly lines: readonly ChartLine[];
  readonly ticks: readonly AxisTick[];
  readonly xLabels: readonly { readonly x: number; readonly text: string }[];
  /** Zero line position, drawn when the axis includes negative values. */
  readonly zeroY: number;
  readonly buckets: readonly string[];
}

/** BR-10: compact axis units ("$5K", "-$1.5M"). */
export function compactMoney(value: number, currency: string): string {
  return new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency,
    notation: 'compact',
    maximumFractionDigits: 1,
  }).format(value);
}

/** BR-12: "Sep 4" for day and week buckets, "Sep 2026" for months. */
export function bucketLabel(start: string, granularity: Granularity): string {
  return granularity === 'month'
    ? `${monthDay(start).split(' ')[0]} ${start.slice(0, 4)}`
    : monthDay(start);
}

/**
 * Round axis limits and ticks that include zero, so a negative estimated profit stays on the chart.
 * With no data the axis spans 0..1.
 */
export function niceAxis(values: readonly number[]): {
  readonly min: number;
  readonly max: number;
  readonly step: number;
} {
  const low = Math.min(0, ...values);
  const high = Math.max(0, ...values);
  if (high === low) {
    return { min: 0, max: 1, step: 1 };
  }
  const rough = (high - low) / 4;
  const power = 10 ** Math.floor(Math.log10(rough));
  const step =
    [1, 2, 2.5, 5, 10].map((factor) => factor * power).find((s) => s >= rough) ?? power * 10;
  return { min: Math.floor(low / step) * step, max: Math.ceil(high / step) * step, step };
}

interface LineSpec {
  readonly id: string;
  readonly name: string;
  readonly tone: 0 | 1 | 2;
  readonly current: (bucket: SeriesBucket) => number;
  readonly previous: (bucket: SeriesBucket) => number;
}

const REVENUE: LineSpec = {
  id: 'revenue',
  name: 'Revenue',
  tone: 0,
  current: (b) => b.revenue,
  previous: (b) => b.previousRevenue,
};
const PROFIT: LineSpec = {
  id: 'profit',
  name: 'Estimated profit',
  tone: 1,
  current: (b) => b.grossProfit,
  previous: (b) => b.previousGrossProfit,
};
const CASH: LineSpec = {
  id: 'cash',
  name: 'Cash collected',
  tone: 2,
  current: (b) => b.collected,
  previous: (b) => b.previousCollected,
};

const TAB_LINES: Readonly<Record<ChartTab, readonly LineSpec[]>> = {
  revenue: [REVENUE, PROFIT],
  profit: [PROFIT],
  cash: [CASH],
};

/** BR-12: lines of a tab for the current period and, with comparison on, the previous one. */
export function buildChart(
  buckets: readonly SeriesBucket[],
  tab: ChartTab,
  compare: boolean,
  granularity: Granularity,
  currency: string,
): ChartModel {
  const specs = TAB_LINES[tab];
  const raw = specs.flatMap((spec) => [
    { spec, previous: false, values: buckets.map(spec.current) },
    ...(compare ? [{ spec, previous: true, values: buckets.map(spec.previous) }] : []),
  ]);
  const axis = niceAxis(raw.flatMap((line) => line.values));
  const plotWidth = CHART_WIDTH - LEFT - RIGHT;
  const plotHeight = CHART_HEIGHT - TOP - BOTTOM;
  const xAt = (index: number): number =>
    buckets.length <= 1 ? LEFT + plotWidth / 2 : LEFT + (index / (buckets.length - 1)) * plotWidth;
  const yAt = (value: number): number =>
    TOP + ((axis.max - value) / (axis.max - axis.min)) * plotHeight;

  const lines = raw.map(({ spec, previous, values }): ChartLine => ({
    id: `${spec.id}-${previous ? 'previous' : 'current'}`,
    label: `${spec.name} (${previous ? 'previous' : 'current'})`,
    dashed: previous,
    tone: spec.tone,
    values,
    points: values
      .map((value, index) => `${xAt(index).toFixed(1)},${yAt(value).toFixed(1)}`)
      .join(' '),
  }));

  const ticks: AxisTick[] = [];
  const tickCount = Math.round((axis.max - axis.min) / axis.step);
  for (let index = 0; index <= tickCount; index++) {
    const value = axis.min + index * axis.step;
    ticks.push({ y: yAt(value), label: compactMoney(value, currency) });
  }

  const labelStep = Math.max(1, Math.ceil(buckets.length / MAX_X_LABELS));
  const xLabels = buckets
    .map((bucket, index) => ({
      x: xAt(index),
      text: bucketLabel(bucket.start, granularity),
      index,
    }))
    .filter(({ index }) => index % labelStep === 0)
    .map(({ x, text }) => ({ x, text }));

  const currentValues = raw.filter((line) => !line.previous).flatMap((line) => line.values);
  return {
    empty: currentValues.every((value) => value === 0),
    lines,
    ticks,
    xLabels,
    zeroY: yAt(0),
    buckets: buckets.map((bucket) => bucketLabel(bucket.start, granularity)),
  };
}
