import { SeriesBucket } from '../models/business-overview.model';
import { bucketLabel, buildChart, compactMoney, niceAxis } from './chart-geometry';

const bucket = (start: string, values: Partial<SeriesBucket> = {}): SeriesBucket => ({
  start,
  end: start,
  revenue: 0,
  grossProfit: 0,
  collected: 0,
  previousRevenue: 0,
  previousGrossProfit: 0,
  previousCollected: 0,
  ...values,
});

describe('Revenue chart geometry', () => {
  // AC-09: axis units, labels, tabs, previous lines, negative profit and the empty state (BR-12).
  it('builds axis, labels and lines per tab with comparison and negative profit', () => {
    expect(compactMoney(5000, 'USD')).toBe('$5K');
    expect(compactMoney(1_500_000, 'USD')).toBe('$1.5M');
    expect(bucketLabel('2026-09-04', 'day')).toBe('Sep 4');
    expect(bucketLabel('2026-09-01', 'month')).toBe('Sep 2026');
    expect(niceAxis([])).toEqual({ min: 0, max: 1, step: 1 });
    expect(niceAxis([0, 17_400])).toMatchObject({ min: 0, max: 20_000 });
    expect(niceAxis([-3000, 9000])).toMatchObject({ min: -5000, max: 10_000 });

    const buckets = [
      bucket('2026-09-01', { revenue: 10_000, grossProfit: -2000, previousRevenue: 8000 }),
      bucket('2026-09-02', { revenue: 12_000, grossProfit: 4000, collected: 500 }),
    ];

    const revenue = buildChart(buckets, 'revenue', true, 'day', 'USD');
    expect(revenue.lines.map((line) => [line.label, line.dashed])).toEqual([
      ['Revenue (current)', false],
      ['Revenue (previous)', true],
      ['Estimated profit (current)', false],
      ['Estimated profit (previous)', true],
    ]);
    expect(revenue.empty).toBe(false);
    expect(revenue.ticks.map((tick) => tick.label)).toEqual(['-$5K', '$0', '$5K', '$10K', '$15K']);
    expect(revenue.ticks.some((tick) => tick.label === '-$5K')).toBe(true);
    expect(revenue.xLabels.map((label) => label.text)).toEqual(['Sep 1', 'Sep 2']);
    expect(revenue.lines[0].points.split(' ')).toHaveLength(2);

    // No previous lines with comparison off; one line on the other tabs.
    expect(buildChart(buckets, 'revenue', false, 'day', 'USD').lines).toHaveLength(2);
    expect(buildChart(buckets, 'profit', false, 'day', 'USD').lines).toHaveLength(1);

    // Empty only when every current value of the tab is zero (the previous period does not count).
    const noCash = buildChart(
      [bucket('2026-09-01', { previousCollected: 900 }), bucket('2026-09-02')],
      'cash',
      true,
      'day',
      'USD',
    );
    expect(noCash.empty).toBe(true);
    expect(buildChart(buckets, 'cash', true, 'day', 'USD').empty).toBe(false);

    // Long periods thin the x labels.
    const many = Array.from({ length: 30 }, (_, index) =>
      bucket(`2026-09-${String(index + 1).padStart(2, '0')}`, { revenue: index }),
    );
    expect(buildChart(many, 'revenue', false, 'day', 'USD').xLabels.length).toBeLessThanOrEqual(8);
  });
});
