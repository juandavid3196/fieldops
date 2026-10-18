import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { SessionService } from '../../../../core/services/session.service';
import {
  TimeDay,
  TimeEntry,
  TimeEntryType,
  TimeSummaryResponse,
} from '../../models/technician-visits.model';
import { formatSeconds, rangeLabel, sectionHeading } from '../../utils/time-format';
import { REFRESH_MS, TechnicianTime } from './time';

@Component({ selector: 'app-visit-stub', template: '' })
class VisitStub {}

const API = 'http://api.test';
const TIME_URL = `${API}/technician/time`;
const ZONE = 'America/Chicago';
const TODAY = '2026-10-14';
const LOAD_ERROR = "We couldn't load your time. Check your connection and try again.";

function addDay(date: string, days: number): string {
  return new Date(Date.parse(`${date}T12:00:00Z`) + days * 86_400_000).toISOString().slice(0, 10);
}

function day(date: string, work = 0, travel = 0, breaks = 0): TimeDay {
  return {
    date,
    workSeconds: work,
    travelSeconds: travel,
    breakSeconds: breaks,
    totalSeconds: work + travel + breaks,
  };
}

function entry(
  id: string,
  type: TimeEntryType,
  startedAt: string,
  endedAt: string | null,
  durationSeconds: number,
  overrides: Partial<TimeEntry> = {},
): TimeEntry {
  return {
    id,
    type,
    startedAt,
    endedAt,
    durationSeconds,
    isOpen: endedAt === null,
    visit: {
      visitId: `v-${id}`,
      displayNumber: 'WO-3091',
      title: 'Kitchen sink leak repair',
      status: 'in_progress',
      visitNumber: 2,
    },
    visitAccessible: true,
    ...overrides,
  };
}

/** Week response of the Monday `from`; `daysData` replaces the zero rows by date. */
function week(
  from: string,
  daysData: TimeDay[] = [],
  entries: TimeEntry[] = [],
): TimeSummaryResponse {
  const days = Array.from({ length: 7 }, (_, index) => {
    const date = addDay(from, index);
    return daysData.find((row) => row.date === date) ?? day(date);
  });
  return summary('week', from, addDay(from, 6), days, entries);
}

function summary(
  view: 'day' | 'week',
  from: string,
  to: string,
  days: TimeDay[],
  entries: TimeEntry[],
): TimeSummaryResponse {
  const sum = (key: 'workSeconds' | 'travelSeconds' | 'breakSeconds') =>
    days.reduce((total, row) => total + row[key], 0);
  const workSeconds = sum('workSeconds');
  const travelSeconds = sum('travelSeconds');
  const breakSeconds = sum('breakSeconds');
  return {
    timezone: ZONE,
    today: TODAY,
    view,
    from,
    to,
    totals: {
      workSeconds,
      travelSeconds,
      breakSeconds,
      totalSeconds: workSeconds + travelSeconds + breakSeconds,
    },
    days,
    entries,
  };
}

function spaced(node: Element | null | undefined): string {
  const parts: string[] = [];
  const walker = document.createTreeWalker(node as Node, NodeFilter.SHOW_TEXT);
  for (let text = walker.nextNode(); text; text = walker.nextNode()) {
    parts.push(text.textContent ?? '');
  }
  return parts.join(' ').replace(/\s+/g, ' ').trim();
}

describe('TechnicianTime', () => {
  let httpTesting: HttpTestingController;
  let harness: RouterTestingHarness;
  let root: HTMLElement;

  async function open(roleCode = 'technician'): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([
          { path: 'today/time', component: TechnicianTime },
          { path: 'today/visits/:id', component: VisitStub },
        ]),
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).setSession({
      user: {
        id: 'u-1',
        firstName: 'Carlos',
        lastName: 'Rivera',
        email: 'c@example.com',
        photoUpdatedAt: null,
      },
      organization: { id: 'o-1', name: 'Acme' },
      role: { code: roleCode, name: 'Role' },
    });
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/today/time');
    root = harness.routeNativeElement as HTMLElement;
  }

  async function settle(): Promise<void> {
    harness.fixture.detectChanges();
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();
  }

  const request = (view: string, date: string | null) =>
    httpTesting.expectOne(
      (r) => r.url === TIME_URL && r.params.get('view') === view && r.params.get('date') === date,
    );
  const click = async (element: Element | null | undefined) => {
    (element as HTMLElement).click();
    await settle();
  };
  const control = (label: string) => root.querySelector(`button[aria-label="${label}"]`);
  const toggle = (label: string) =>
    Array.from(root.querySelectorAll('.time__toggle-button')).find(
      (button) => button.textContent?.trim() === label,
    );
  const label = () => spaced(root.querySelector('.time__label'));
  const rows = () => Array.from(root.querySelectorAll<HTMLElement>('.time__row'));
  const dialog = () => document.body.querySelector('.p-dialog');

  afterEach(() => httpTesting.verify());

  it('loads the current week, toggles Day/Week and navigates with the BR-06 date rules; superseded requests are cancelled (AC-06)', async () => {
    await open();
    expect(root.querySelector('[aria-busy="true"]')).not.toBeNull();
    httpTesting
      .expectOne(
        (r) => r.url === TIME_URL && r.params.get('view') === 'week' && !r.params.has('date'),
      )
      .flush(week('2026-10-12'));
    await settle();

    expect(spaced(root.querySelector('h1'))).toBe('Time summary');
    expect(toggle('Week')?.getAttribute('aria-pressed')).toBe('true');
    expect(label()).toBe('Oct 12 – 18');

    // Day keeps today when the week contains it; Day navigation is one day at a time.
    await click(toggle('Day'));
    request('day', TODAY).flush(summary('day', TODAY, TODAY, [day(TODAY)], []));
    await settle();
    expect(label()).toBe('Wed, Oct 14');
    expect(spaced(root.querySelector('.time__empty'))).toBe('No time recorded for this day.');
    await click(control('Next day'));
    request('day', '2026-10-15').flush(
      summary('day', '2026-10-15', '2026-10-15', [day('2026-10-15')], []),
    );
    await settle();
    expect(label()).toBe('Thu, Oct 15');

    // Week shows the week of the selected date; a repeated click supersedes the in-flight request.
    await click(toggle('Week'));
    request('week', '2026-10-15').flush(week('2026-10-12'));
    await settle();
    await click(control('Next week'));
    const replaced = request('week', '2026-10-22');
    await click(control('Next week'));
    expect(replaced.cancelled).toBe(true);
    request('week', '2026-10-29').flush(week('2026-10-26'));
    await settle();
    expect(label()).toBe('Oct 26 – Nov 1');
    expect(spaced(root.querySelector('.time__empty'))).toBe('No time recorded for this week.');
    expect(spaced(root.querySelector('.time__total'))).toBe('0m');

    // Day from a week without today keeps that week's Monday; future ranges stay navigable.
    await click(toggle('Day'));
    request('day', '2026-10-26').flush(
      summary('day', '2026-10-26', '2026-10-26', [day('2026-10-26')], []),
    );
    await settle();
    expect(label()).toBe('Mon, Oct 26');
    expect(control('Previous day')).not.toBeNull();

    // Label, heading and duration formats (month and year boundaries, current-year rule).
    expect([
      rangeLabel('week', '2026-09-28', '2026-10-04', TODAY),
      rangeLabel('week', '2026-12-28', '2027-01-03', TODAY),
      rangeLabel('week', '2027-02-01', '2027-02-07', TODAY),
      rangeLabel('day', '2027-01-01', '2027-01-01', TODAY),
      sectionHeading(TODAY, TODAY),
      sectionHeading('2026-10-13', TODAY),
      sectionHeading('2026-10-12', TODAY),
      [0, 59, 60, 3600, 15600, 117_900].map(formatSeconds).join('|'),
    ]).toEqual([
      'Sep 28 – Oct 4',
      'Dec 28 – Jan 3, 2027',
      'Feb 1 – 7, 2027',
      'Fri, Jan 1, 2027',
      'Today · Wednesday',
      'Yesterday · Tuesday',
      'Monday, Oct 12',
      '0m|0m|1m|1h 0m|4h 20m|32h 45m',
    ]);
  });

  it('shows totals, the stacked week chart with per-day text alternatives and zero days, and no chart in Day view (AC-07)', async () => {
    await open();
    httpTesting
      .expectOne((r) => r.url === TIME_URL && !r.params.has('date'))
      .flush(week('2026-10-12', [day('2026-10-12', 13_200, 1800, 600), day('2026-10-14', 3600)]));
    await settle();

    expect(spaced(root.querySelector('.time__total'))).toBe('5h 20m');
    expect(spaced(root.querySelector('.time__total-label'))).toBe('Total recorded');
    expect(Array.from(root.querySelectorAll('.time__tile'), (tile) => spaced(tile))).toEqual([
      'Work 4h 40m',
      'Travel 30m',
      'Break 10m',
    ]);
    const bars = Array.from(root.querySelectorAll<HTMLElement>('.time__bar'));
    expect(bars).toHaveLength(7);
    expect(spaced(bars[0].querySelector('.time__sr'))).toBe(
      'Mon: total 4h 20m, work 3h 40m, travel 30m, break 10m',
    );
    expect(spaced(bars[1].querySelector('.time__sr'))).toBe(
      'Tue: total 0m, work 0m, travel 0m, break 0m',
    );
    // Bars scale to the highest day; a zero day keeps an empty baseline with "0m".
    expect(bars[0].querySelector<HTMLElement>('.time__bar-stack')?.style.blockSize).toBe('5.5rem');
    expect(bars[1].querySelector<HTMLElement>('.time__bar-stack')?.style.blockSize).toBe('0rem');
    expect(spaced(bars[1].querySelector('.time__bar-total'))).toBe('0m');
    expect(spaced(root.querySelector('.time__legend'))).toBe('Work Travel Break');

    await click(Array.from(root.querySelectorAll('.time__toggle-button')).at(0));
    request('day', TODAY).flush(summary('day', TODAY, TODAY, [day(TODAY, 3600)], []));
    await settle();
    expect(root.querySelector('.time__chart')).toBeNull();
    expect(spaced(root.querySelector('.time__total'))).toBe('1h 0m');
  });

  it('groups entries by local start day, shows Break and open entries and a read-only detail gated by visitAccessible (AC-08)', async () => {
    await open();
    const entries = [
      entry('open', 'work', '2026-10-14T18:00:00Z', null, 600),
      entry('break', 'break', '2026-10-14T14:17:00Z', '2026-10-14T14:47:00Z', 1800, {
        visit: {
          visitId: 'v-b',
          displayNumber: 'WO-3104',
          title: 'Water heater inspection',
          status: 'paused',
          visitNumber: 1,
        },
      }),
      entry('travel', 'travel', '2026-10-13T14:05:00Z', '2026-10-13T14:28:00Z', 1380, {
        visitAccessible: false,
      }),
      entry('older', 'work', '2026-10-12T14:00:00Z', '2026-10-12T15:00:00Z', 3600),
    ];
    httpTesting
      .expectOne((r) => r.url === TIME_URL && !r.params.has('date'))
      .flush(
        week(
          '2026-10-12',
          [day(TODAY, 600, 0, 1800), day('2026-10-13', 0, 1380), day('2026-10-12', 3600)],
          entries,
        ),
      );
    await settle();

    expect(Array.from(root.querySelectorAll('.time__heading'), (h) => spaced(h))).toEqual([
      'Today · Wednesday',
      'Yesterday · Tuesday',
      'Monday, Oct 12',
    ]);
    expect(Array.from(root.querySelectorAll('.time__section-total'), (t) => spaced(t))).toEqual([
      '40m',
      '23m',
      '1h 0m',
    ]);
    expect(spaced(rows()[0])).toBe(
      'Kitchen sink leak repair Work In progress #WO-3091 1:00 PM – now 10m',
    );
    expect(spaced(rows()[1])).toBe(
      'Break Break Water heater inspection #WO-3104 9:17 AM – 9:47 AM 30m',
    );
    expect(spaced(rows()[2])).toBe(
      'Kitchen sink leak repair Travel #WO-3091 9:05 AM – 9:28 AM 23m',
    );
    expect(spaced(root.querySelector('.time__list + .time__notice, .time__notice'))).toBe(
      'Time is recorded automatically while you travel, work or pause a job.',
    );

    // Detail: read-only, Open job only when visitAccessible, focus returns to the row.
    rows()[0].focus();
    await click(rows()[0]);
    const panel = () => spaced(dialog());
    expect(panel()).toContain('Work order Kitchen sink leak repair #WO-3091');
    expect(panel()).toContain('Visit 2');
    expect(panel()).toContain('Date Wed, Oct 14, 2026');
    expect(panel()).toContain('End In progress');
    expect(panel()).toContain(
      "Time entries can't be edited here. Contact your manager for corrections.",
    );
    expect(dialog()?.querySelector('a')?.getAttribute('href')).toBe('/today/visits/v-open');
    expect(dialog()?.querySelectorAll('button')).toHaveLength(2);
    expect(Array.from(dialog()?.querySelectorAll('button') ?? [], (b) => spaced(b))).not.toContain(
      'Edit',
    );
    await click(
      Array.from(dialog()?.querySelectorAll('button') ?? []).find((b) => spaced(b) === 'Close'),
    );
    expect(dialog()).toBeNull();
    expect(document.activeElement).toBe(rows()[0]);

    await click(rows()[2]);
    expect(dialog()?.querySelector('a')).toBeNull();
    expect(panel()).toContain('Travel Visit');
    await click(dialog()?.querySelector('button[aria-label="Close"]'));
    expect(dialog()).toBeNull();
  });

  it('shows error with Retry, keeps previous data on a failed navigation, falls back from 400 and shows permission states (AC-09, AC-02)', async () => {
    await open();
    const first = () => httpTesting.expectOne((r) => r.url === TIME_URL && !r.params.has('date'));
    first().flush(null, { status: 500, statusText: 'Server Error' });
    await settle();
    expect(spaced(root.querySelector('p-message'))).toContain(LOAD_ERROR);
    expect(root.querySelector('.time__card')).toBeNull();

    await click(root.querySelector('p-message button'));
    first().flush(week('2026-10-12', [day('2026-10-12', 3600)]));
    await settle();
    expect(spaced(root.querySelector('.time__total'))).toBe('1h 0m');

    // Failed navigation: previous range and totals stay with the inline error; Retry reloads it.
    await click(control('Next week'));
    request('week', '2026-10-21').flush(null, { status: 500, statusText: 'Server Error' });
    await settle();
    expect(spaced(root.querySelector('p-message'))).toContain(LOAD_ERROR);
    expect(label()).toBe('Oct 12 – 18');
    expect(spaced(root.querySelector('.time__total'))).toBe('1h 0m');
    expect(control('Next week')).not.toBeNull();
    await click(root.querySelector('p-message button'));
    request('week', '2026-10-21').flush(week('2026-10-19'));
    await settle();
    expect(root.querySelector('p-message')).toBeNull();
    expect(label()).toBe('Oct 19 – 25');

    // Invalid date/view (400) falls back to the current week.
    await click(control('Next week'));
    request('week', '2026-10-28').flush(
      { status: 400, code: 'errors.date', title: 'Choose a valid date.' },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();
    first().flush(week('2026-10-12'));
    await settle();
    expect(label()).toBe('Oct 12 – 18');
    expect(root.querySelector('p-message')).toBeNull();
  });

  it.each([
    [
      'technician_profile_not_linked',
      404,
      "Your team profile isn't linked yet. Contact your manager.",
    ],
    ['technician_inactive', 403, 'Your technician profile is inactive. Contact your manager.'],
    ['forbidden', 403, "You don't have access to Today's jobs."],
  ])('shows the %s permission state without data (AC-02)', async (code, status, message) => {
    await open();
    httpTesting
      .expectOne((r) => r.url === TIME_URL)
      .flush({ status, code, title: 'x', type: 'about:blank' }, { status, statusText: 'Error' });
    await settle();
    expect(spaced(root.querySelector('.time__access'))).toBe(message);
    expect(root.querySelector('.time__card')).toBeNull();
  });

  it('refreshes open-entry durations at least every minute while visible (AC-08, AC-09)', async () => {
    vi.useFakeTimers();
    try {
      await open();
      httpTesting
        .expectOne((r) => r.url === TIME_URL && !r.params.has('date'))
        .flush(
          week(
            '2026-10-12',
            [day(TODAY, 600)],
            [entry('open', 'work', '2026-10-14T18:00:00Z', null, 600)],
          ),
        );
      await settle();
      expect(spaced(rows()[0])).toContain('10m');

      await vi.advanceTimersByTimeAsync(REFRESH_MS);
      request('week', TODAY).flush(
        week(
          '2026-10-12',
          [day(TODAY, 660)],
          [entry('open', 'work', '2026-10-14T18:00:00Z', null, 660)],
        ),
      );
      await settle();
      expect(spaced(rows()[0])).toContain('11m');
      expect(root.querySelector('[aria-busy="true"]')).toBeNull();
    } finally {
      vi.useRealTimers();
    }
  });
});
