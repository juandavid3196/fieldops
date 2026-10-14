import { DayVisit, Attention, TechnicianAvailability } from '../models/dashboard.model';
import { resetField } from '../services/dashboard-page.store';
import { boardLink } from './dashboard-params';
import { agoLabel, hiddenTooLong, shouldAutoRefresh } from './dashboard-refresh';
import {
  attentionView,
  onTimeTrend,
  rowAction,
  statusChip,
  technicianRow,
  timing,
  varianceText,
} from './dashboard-view';

const visit = (overrides: Partial<DayVisit>): DayVisit => ({
  visitId: 'v-1',
  workOrderId: 'w-1',
  displayNumber: 'WO-1056',
  title: 'Toilet replacement',
  customerName: 'Jennifer Walsh',
  customerPhone: null,
  branchId: 'b-1',
  status: 'scheduled',
  statusGroup: 'scheduled',
  isDelayed: false,
  delayMinutes: null,
  isAtRisk: false,
  scheduledStart: '2026-09-20T14:30:00Z',
  scheduledEnd: '2026-09-20T16:30:00Z',
  actualCompletedAt: null,
  projectedEnd: null,
  technician: null,
  otherTechnicianCount: 0,
  ...overrides,
});

describe('Dashboard derived texts and actions', () => {
  it('derives row chips, timing and actions per status, and the shared links (AC-06, AC-07, AC-09, AC-10, AC-19)', () => {
    const rows: [Partial<DayVisit>, string, string, string | undefined, string, string | null][] = [
      // overrides, chip, timing label, timing note, action, action target
      [{ status: 'scheduled' }, 'Scheduled', 'Starts 2:30 PM', undefined, 'Assign', '/schedule'],
      [
        { status: 'assigned', isDelayed: true, delayMinutes: 38 },
        'Delayed',
        'Starts 2:30 PM',
        '+38 min',
        'Reassign',
        '/schedule',
      ],
      [{ status: 'assigned' }, 'Scheduled', 'Starts 2:30 PM', undefined, 'View', '/jobs'],
      [
        { status: 'on_the_way', customerPhone: '(555) 010-2000' },
        'On the way',
        'Starts 2:30 PM',
        'On time',
        'Contact',
        'tel:5550102000',
      ],
      [
        { status: 'in_progress', statusGroup: 'active', projectedEnd: '2026-09-20T17:00:00Z' },
        'In progress',
        'Ends ~5:00 PM',
        'On time',
        'View',
        '/jobs',
      ],
      [
        {
          status: 'completed',
          statusGroup: 'completed',
          actualCompletedAt: '2026-09-20T15:45:00Z',
        },
        'Completed',
        'Completed 3:45 PM',
        undefined,
        'View',
        '/jobs',
      ],
    ];
    for (const [overrides, chip, label, note, action, target] of rows) {
      const item = visit(overrides);
      const result = rowAction(item, '2026-09-20');
      expect(statusChip(item).label).toBe(chip);
      expect(timing(item, 'UTC').label).toBe(label);
      expect(timing(item, 'UTC').note?.text).toBe(note);
      expect(result.label).toBe(action);
      expect(result.href ?? result.link?.path[0]).toContain(target?.replace('/', '') ?? '');
    }
    expect(rowAction(visit({ status: 'scheduled' }), '2026-09-20').link).toEqual({
      path: ['/schedule'],
      queryParams: { branch: 'b-1', view: 'day', date: '2026-09-20', visit: 'v-1' },
    });
    expect(boardLink(null, '2026-09-20').queryParams).toEqual({ view: 'day', date: '2026-09-20' });

    // Needs attention: titles, subtitles and actions for the four types.
    const base: Attention = {
      type: 'delayed',
      visitId: 'v-1',
      workOrderId: 'w-1',
      displayNumber: 'WO-1056',
      title: 'Toilet replacement',
      visitStatus: 'assigned',
      branchId: 'b-1',
      at: '2026-09-20T16:00:00Z',
      minutes: 38,
      technician: null,
    };
    const capacity: Attention = {
      ...base,
      type: 'over_capacity',
      visitId: null,
      workOrderId: null,
      displayNumber: null,
      title: null,
      visitStatus: null,
      at: null,
      technician: {
        id: 't-1',
        fullName: 'Ana Lopez',
        scheduledMinutes: 396,
        availableMinutes: 360,
        percent: 110,
      },
    };
    const views = [
      attentionView(base, 0, '2026-09-20', 'UTC'),
      attentionView(
        { ...base, type: 'unassigned', visitStatus: 'scheduled' },
        1,
        '2026-09-20',
        'UTC',
      ),
      attentionView(
        { ...base, type: 'over_estimate', visitStatus: 'in_progress', minutes: 45 },
        2,
        '2026-09-20',
        'UTC',
      ),
      attentionView(capacity, 3, '2026-09-20', 'UTC'),
    ];
    expect(views.map((view) => [view.title, view.action.label, view.menu.label])).toEqual([
      ['WO-1056 delayed by 38 min', 'Reassign', 'View job'],
      ['WO-1056 has no technician assigned', 'Assign', 'View job'],
      ['WO-1056 estimated duration exceeded', 'View job', 'View job'],
      ['Technician Ana Lopez at 110% capacity', 'Balance workload', 'View team'],
    ]);
    expect(views.map((view) => view.subtitle)).toEqual([
      'Toilet replacement',
      'Toilet replacement · Starts at 4:00 PM',
      'Toilet replacement · 45 min over estimate',
      '6.6/6 hrs scheduled · Consider rebalancing',
    ]);

    // Technician row: status/next only today, workload states, skills.
    const tech: TechnicianAvailability = {
      id: 't-1',
      fullName: 'Ana Lopez',
      initials: 'AL',
      colorHex: 'red',
      skills: ['Plumbing', 'HVAC'],
      extraSkillCount: 2,
      currentStatus: 'available',
      scheduledMinutes: 0,
      availableMinutes: 480,
      percent: 0,
      nextAvailable: null,
    };
    expect(technicianRow(tech, true, 'UTC')).toMatchObject({
      skills: 'Plumbing, HVAC +2',
      color: null,
      next: 'Now',
      status: { label: 'Available' },
      workload: { text: '0%', over: false },
    });
    expect(
      technicianRow(
        { ...tech, availableMinutes: 0, scheduledMinutes: 60, percent: null },
        false,
        'UTC',
      ),
    ).toMatchObject({ next: '—', status: null, workload: { text: 'No availability', over: true } });

    // Trend, variance and refresh helpers.
    expect([
      onTimeTrend(91, 88)?.text,
      onTimeTrend(80, 85)?.text,
      onTimeTrend(70, 70)?.text,
      onTimeTrend(null, 70),
    ]).toEqual(['+3% vs last week', '−5% vs last week', 'Same as last week', null]);
    expect([varianceText(100, 108), varianceText(75, 70), varianceText(60, 60)]).toEqual([
      '+8 min',
      '−5 min',
      'On estimate',
    ]);
    expect([agoLabel(0, 59_000), agoLabel(0, 120_000), agoLabel(null, 1)]).toEqual([
      'Updated just now',
      'Updated 2 min ago',
      '',
    ]);
    const state = { visible: true, loading: false, lastAttemptAt: 0, now: 60_000 };
    expect([
      shouldAutoRefresh(state),
      shouldAutoRefresh({ ...state, visible: false }),
      shouldAutoRefresh({ ...state, loading: true }),
      shouldAutoRefresh({ ...state, now: 59_999 }),
      hiddenTooLong(0, 60_001),
      hiddenTooLong(0, 60_000),
    ]).toEqual([true, false, false, false, true, false]);

    // Rejected URL values reset to their defaults only when they differ from them.
    const route = {
      branchId: 'b-1',
      date: '2026-09-20',
      technicianId: 't-1',
      categoryId: 'c-1',
      period: '30' as const,
    };
    const error = (status: number, key?: string) =>
      ({
        kind: 'validation',
        status,
        message: '',
        fieldErrors: key ? { [key]: ['x'] } : {},
      }) as const;
    expect([
      resetField(error(404), route, '2026-09-20'),
      resetField(error(404), { ...route, technicianId: null }, '2026-09-20'),
      resetField(error(400, 'categoryId'), route, '2026-09-20'),
      resetField(error(400, 'date'), route, '2026-09-20'),
      resetField(error(400, 'date'), route, '2026-09-21'),
      resetField(error(400, 'period'), route, '2026-09-20'),
      resetField(error(500), route, '2026-09-20'),
    ]).toEqual(['technician', 'branch', 'category', null, 'date', 'period', null]);
  });
});
