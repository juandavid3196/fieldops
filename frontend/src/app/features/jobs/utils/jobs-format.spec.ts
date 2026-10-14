import { JobRow, JobsView } from '../models/jobs.model';
import {
  JOB_STATUS_CHIPS,
  TAB_STATUSES,
  isCanonicalJobsUrl,
  jobActionTargets,
  jobsViewParams,
  propertyLocation,
  resolveJobsView,
  scheduleText,
  updatedText,
} from './jobs-format';

const row = (patch: Partial<JobRow> = {}): JobRow => ({
  id: 'wo-1',
  displayNumber: 'WO-7',
  title: 'Kitchen',
  customerName: 'Marina',
  property: { name: 'Home', postalCode: null, city: 'Medellin' },
  branch: { id: 'br-1', name: 'Central' },
  timezone: 'America/Chicago',
  status: 'scheduled',
  priority: 'normal',
  currentVisit: {
    id: 'v-1',
    scheduledStart: '2026-10-09T13:30:00Z',
    scheduledEnd: '2026-10-09T15:30:00Z',
  },
  technician: null,
  updatedAt: '2026-10-09T13:30:00Z',
  createdAt: '2026-10-01T13:30:00Z',
  actions: ['view'],
  ...patch,
});

const visit = (
  scheduledStart: string | null,
  scheduledEnd: string | null,
): JobRow['currentVisit'] => ({
  id: 'v-1',
  scheduledStart,
  scheduledEnd,
});

describe('Jobs workspace pure rules', () => {
  it('normalizes URL state to its canonical form with defaults for invalid values (BR-06, BR-16, AC-05, AC-17)', () => {
    const view = (
      raw: Record<string, string>,
      branches: string[] | null = ['b1', 'b2'],
    ): JobsView => resolveJobsView((key) => raw[key] ?? null, branches, null);

    expect(view({})).toEqual({
      tab: 'all',
      q: '',
      branch: null,
      status: null,
      priority: null,
      sort: 'newest',
      page: 1,
      pageSize: 10,
    });
    // Status outside its tab, unknown tab/sort/priority, bad page and page size, long search.
    const invalid = view({
      tab: 'unscheduled',
      status: 'paused',
      priority: 'x',
      sort: 'z',
      page: '0',
      pageSize: '7',
      q: 'a'.repeat(101),
      branch: 'foreign',
    });
    expect(jobsViewParams(invalid)).toEqual({
      tab: 'unscheduled',
      q: null,
      branch: null,
      status: null,
      priority: null,
      sort: null,
      page: null,
      pageSize: null,
    });
    const valid = view({
      tab: 'active',
      status: 'paused',
      priority: 'high',
      sort: 'schedule',
      page: '3',
      pageSize: '25',
      q: ' wo-7 ',
      branch: 'b2',
    });
    expect(valid).toMatchObject({
      tab: 'active',
      status: 'paused',
      page: 3,
      pageSize: 25,
      q: 'wo-7',
    });
    expect(valid.branch).toBe('b2');
    expect(
      isCanonicalJobsUrl(
        (key) => ({ tab: 'active' })[key as 'tab'] ?? null,
        view({ tab: 'active' }),
      ),
    ).toBe(true);
    expect(isCanonicalJobsUrl(() => 'bogus', view({}))).toBe(false);
    // The only branch is the default, so its explicit parameter is dropped.
    expect(
      resolveJobsView((key) => (key === 'branch' ? 'b1' : null), ['b1'], 'b1').branch,
    ).toBeNull();
    expect(TAB_STATUSES.completed).toContain('needs_correction');
  });

  it('maps status codes to fixed labels and tones and builds the BR-14 row targets (BR-05, BR-14, AC-13, AC-14)', () => {
    expect(JOB_STATUS_CHIPS.assigned).toEqual({ label: 'Assigned', tone: 'purple' });
    expect(JOB_STATUS_CHIPS.needs_correction).toEqual({
      label: 'Needs correction',
      tone: 'danger',
    });
    expect(JOB_STATUS_CHIPS.approved_for_billing.label).toBe('Approved for billing');

    const targets = jobActionTargets(
      row({ actions: ['view', 'schedule_visit', 'open_in_schedule', 'review_completion'] }),
    );
    expect(targets.map((target) => [target.label, target.commands, target.queryParams])).toEqual([
      ['View job', ['/jobs', 'wo-1'], null],
      ['Schedule visit', ['/schedule'], { branch: 'br-1', visit: 'v-1' }],
      [
        'Open in schedule',
        ['/schedule'],
        { branch: 'br-1', view: 'day', date: '2026-10-09', visit: 'v-1' },
      ],
      ['Review completion', ['/invoices', 'review'], { workOrderId: 'wo-1' }],
    ]);
    // Cancelled jobs offer only what the server sent.
    expect(jobActionTargets(row({ status: 'cancelled', actions: ['view'] }))).toHaveLength(1);
  });

  it('formats schedule, updated and property text in the right time zone (BR-12, BR-13, AC-11, AC-12)', () => {
    const now = new Date('2026-10-14T15:00:00Z');
    const schedule = (start: string | null, end: string | null): string =>
      scheduleText(row({ currentVisit: visit(start, end) }), now);

    expect(schedule('2026-10-14T18:00:00Z', '2026-10-14T20:00:00Z')).toBe('Today · 1:00–3:00 PM');
    expect(schedule('2026-10-15T14:00:00Z', '2026-10-15T16:00:00Z')).toBe(
      'Tomorrow · 9:00–11:00 AM',
    );
    expect(schedule('2026-10-09T13:30:00Z', '2026-10-09T15:30:00Z')).toBe('Oct 9 · 8:30–10:30 AM');
    expect(schedule('2026-10-20T16:00:00Z', '2026-10-20T19:00:00Z')).toBe(
      'Oct 20 · 11:00 AM–2:00 PM',
    );
    expect(schedule('2025-12-30T16:00:00Z', '2025-12-30T17:00:00Z')).toBe(
      'Dec 30, 2025 · 10:00–11:00 AM',
    );
    expect(schedule(null, null)).toBe('Not scheduled');
    expect(scheduleText(row({ currentVisit: null }), now)).toBe('Not scheduled');

    const zone = 'America/Chicago';
    expect(updatedText('2026-10-14T14:59:30Z', zone, now)).toBe('Just now');
    expect(updatedText('2026-10-14T14:48:00Z', zone, now)).toBe('12 min ago');
    expect(updatedText('2026-10-14T13:00:00Z', zone, now)).toBe('Today');
    expect(updatedText('2026-10-13T20:00:00Z', zone, now)).toBe('Yesterday');
    expect(updatedText('2026-10-09T13:30:00Z', zone, now)).toBe('Oct 9');
    expect(updatedText('2025-03-01T13:30:00Z', zone, now)).toBe('Mar 1, 2025');

    expect(propertyLocation(row())).toBe('Medellin');
    expect(
      propertyLocation(row({ property: { name: 'Home', postalCode: '76968', city: 'Medellin' } })),
    ).toBe('76968 Medellin');
  });
});
