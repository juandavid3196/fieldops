import { BusinessOptions } from '../models/business-overview.model';
import { periodLabel, presetPeriod, resolveBusinessRoute } from './business-params';

const NOW = new Date('2026-09-20T15:00:00Z');
const JANUARY = new Date('2026-01-05T15:00:00Z');

const options = (branchCount: number): BusinessOptions => ({
  timezone: 'America/Chicago',
  currency: 'USD',
  today: '2026-09-20',
  marginTarget: 40,
  branches: Array.from({ length: branchCount }, (_, index) => ({
    id: `b-${index + 1}`,
    name: `Branch ${index + 1}`,
  })),
});

const params = (query: Record<string, string>): { get(name: string): string | null } => ({
  get: (name) => query[name] ?? null,
});

describe('Business overview URL state', () => {
  // AC-05, AC-08, AC-18: presets, custom range, invalid values, branch and compare (BR-03, BR-04).
  it.each([
    ['defaults to this month', {}, NOW, 2, '2026-09-01', '2026-09-20', false],
    ['keeps a preset', { range: 'last_30_days' }, NOW, 2, '2026-08-22', '2026-09-20', false],
    ['last month', { range: 'last_month' }, NOW, 2, '2026-08-01', '2026-08-31', false],
    [
      'quarter start in January',
      { range: 'this_quarter' },
      JANUARY,
      2,
      '2026-01-01',
      '2026-01-05',
      false,
    ],
    [
      'last month across the year',
      { range: 'last_month' },
      JANUARY,
      2,
      '2025-12-01',
      '2025-12-31',
      false,
    ],
    ['this year', { range: 'this_year' }, NOW, 2, '2026-01-01', '2026-09-20', false],
    ['unknown preset resets', { range: 'foo' }, NOW, 2, '2026-09-01', '2026-09-20', true],
    [
      'valid custom range',
      { range: 'custom', from: '2026-09-01', to: '2026-09-10' },
      NOW,
      2,
      '2026-09-01',
      '2026-09-10',
      false,
    ],
    [
      'custom range of exactly 366 days',
      { range: 'custom', from: '2025-09-20', to: '2026-09-20' },
      NOW,
      2,
      '2025-09-20',
      '2026-09-20',
      false,
    ],
    [
      'custom range over 366 days resets',
      { range: 'custom', from: '2025-09-19', to: '2026-09-20' },
      NOW,
      2,
      '2026-09-01',
      '2026-09-20',
      true,
    ],
    [
      'reversed custom range resets',
      { range: 'custom', from: '2026-09-10', to: '2026-09-01' },
      NOW,
      2,
      '2026-09-01',
      '2026-09-20',
      true,
    ],
    [
      'future custom end resets',
      { range: 'custom', from: '2026-09-10', to: '2026-09-21' },
      NOW,
      2,
      '2026-09-01',
      '2026-09-20',
      true,
    ],
    [
      'incomplete custom range resets',
      { range: 'custom', from: '2026-09-10' },
      NOW,
      2,
      '2026-09-01',
      '2026-09-20',
      true,
    ],
    [
      'stray dates on a preset are removed',
      { range: 'this_month', from: '2026-09-10' },
      NOW,
      2,
      '2026-09-01',
      '2026-09-20',
      true,
    ],
  ])('%s', (_name, query, now, branches, from, to, normalized) => {
    const resolved = resolveBusinessRoute(params(query), options(branches), now);
    expect([resolved.route.from, resolved.route.to, resolved.normalized]).toEqual([
      from,
      to,
      normalized,
    ]);
  });

  it('resolves branch and comparison, and formats the subtitle period', () => {
    const resolve = (query: Record<string, string>, branches = 2) =>
      resolveBusinessRoute(params(query), options(branches), NOW);

    expect(resolve({ branch: 'b-2' }).route.branchId).toBe('b-2');
    // An unknown branch resets to All; with one branch it is selected and written to the URL.
    expect(resolve({ branch: 'b-9' })).toMatchObject({
      route: { branchId: null },
      normalized: true,
    });
    expect(resolve({}, 1)).toMatchObject({
      route: { branchId: 'b-1' },
      query: { branch: 'b-1' },
      normalized: true,
    });
    // compare=false is the only value kept; anything else means on and is removed.
    expect(resolve({ compare: 'false' })).toMatchObject({
      route: { compare: false },
      normalized: false,
    });
    expect(resolve({ compare: '0' })).toMatchObject({ route: { compare: true }, normalized: true });

    expect(presetPeriod('this_month', '2026-02-10')).toEqual({
      from: '2026-02-01',
      to: '2026-02-10',
    });
    expect(periodLabel('2026-09-01', '2026-09-20')).toBe('Sep 1 – 20, 2026');
    expect(periodLabel('2026-08-15', '2026-09-20')).toBe('Aug 15, 2026 – Sep 20, 2026');
    expect(periodLabel('2025-12-15', '2026-01-02')).toBe('Dec 15, 2025 – Jan 2, 2026');
  });
});
