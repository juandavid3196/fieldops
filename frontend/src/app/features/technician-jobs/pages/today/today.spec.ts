import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { SessionService } from '../../../../core/services/session.service';
import { TodayResponse, TodayVisit, TodayVisitStatus } from '../../models/technician-visits.model';
import { ALL_DONE_MESSAGE, NO_JOBS_MESSAGE, NO_STARTABLE_MESSAGE, Today } from './today';

const API = 'http://api.test';
const TODAY_URL = `${API}/technician/today`;
// Far from any CI device zone: proves formatting uses the response zone.
const ZONE = 'Pacific/Auckland';

@Component({ template: '' })
class Stub {}

function visit(
  id: string,
  status: TodayVisitStatus,
  overrides: Partial<TodayVisit> = {},
): TodayVisit {
  return {
    visitId: id,
    visitNumber: 1,
    workOrderId: `wo-${id}`,
    displayNumber: `WO-${id}`,
    title: `Job ${id}`,
    status,
    start: '2026-10-07T09:30:00+13:00',
    end: '2026-10-07T11:30:00+13:00',
    arrivalWindowStart: '2026-10-07T09:30:00+13:00',
    arrivalWindowEnd: '2026-10-07T10:30:00+13:00',
    priority: 2,
    serviceCategory: 'Plumbing',
    customerName: 'Sofia Martinez',
    phone: '(512) 555-0100',
    address: {
      line1: '1842 Oak Street',
      line2: null,
      city: 'Austin',
      stateRegion: 'TX',
      postalCode: '78704',
      countryCode: 'US',
    },
    latitude: 30.25,
    longitude: -97.75,
    plannedMaterialsCount: 2,
    isPrimary: true,
    ...overrides,
  };
}

function response(
  visits: TodayVisit[],
  metrics: Partial<TodayResponse['metrics']>,
  nextVisitId: string | null,
): TodayResponse {
  const completed = visits.filter((v) => ['completed', 'approved'].includes(v.status)).length;
  return {
    date: '2026-10-07',
    timezone: ZONE,
    technician: { firstName: 'Carlos', initials: 'CR', colorHex: null },
    metrics: {
      jobs: visits.length,
      scheduledMinutes: 0,
      completed,
      remaining: visits.length - completed,
      ...metrics,
    },
    nextVisitId,
    visits,
  };
}

describe('Today', () => {
  let httpTesting: HttpTestingController;
  let fixture: ComponentFixture<Today>;

  function create(roleCode = 'technician'): void {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([{ path: 'today/visits/:visitId', component: Stub }]),
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).setSession({
      user: { id: 'u-1', firstName: 'Carlos', lastName: 'Rivera', email: 'c@example.com' },
      organization: { id: 'o-1', name: 'Acme' },
      role: { code: roleCode, name: 'Role' },
    });
    fixture = TestBed.createComponent(Today);
  }

  async function load(body: TodayResponse): Promise<void> {
    create();
    httpTesting.expectOne({ method: 'GET', url: TODAY_URL }).flush(body);
    await settle();
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const root = () => fixture.nativeElement as HTMLElement;
  const text = (selector: string) =>
    root().querySelector(selector)?.textContent?.replace(/\s+/g, ' ').trim();
  const refreshButton = () => root().querySelector<HTMLButtonElement>('.today__refresh')!;

  afterEach(() => {
    vi.useRealTimers();
    httpTesting.verify();
  });

  it.each<[string, TodayResponse, string[], string, string]>([
    [
      'in progress ahead of an assigned visit',
      response(
        [
          visit('1', 'completed'),
          visit('2', 'in_progress'),
          visit('3', 'assigned'),
          visit('4', 'scheduled'),
        ],
        { scheduledMinutes: 450 },
        '2',
      ),
      ['4jobs', '7.5scheduled hours', '3remaining'],
      '1 of 4 completed 25%',
      'IN PROGRESS • 9:30 AM – 11:30 AM',
    ],
    [
      'a single scheduled visit',
      response([visit('1', 'scheduled')], { scheduledMinutes: 60 }, '1'),
      ['1job', '1scheduled hour', '1remaining'],
      '0 of 1 completed 0%',
      'NEXT • 9:30 AM – 11:30 AM',
    ],
    [
      'all completed',
      response([visit('1', 'completed'), visit('2', 'approved')], { scheduledMinutes: 480 }, null),
      ['2jobs', '8scheduled hours', '0remaining'],
      '2 of 2 completed 100%',
      ALL_DONE_MESSAGE,
    ],
    [
      'completed plus needs correction',
      response(
        [visit('1', 'completed'), visit('2', 'needs_correction')],
        { scheduledMinutes: 240 },
        null,
      ),
      ['2jobs', '4scheduled hours', '1remaining'],
      '1 of 2 completed 50%',
      NO_STARTABLE_MESSAGE,
    ],
    [
      'no visits',
      response([], {}, null),
      ['0jobs', '0scheduled hours', '0remaining'],
      '',
      NO_JOBS_MESSAGE,
    ],
  ])(
    'shows metrics, progress and the next job or empty text for %s (AC-07, AC-08, AC-12)',
    async (_name, body, metricTexts, progress, nextOrEmpty) => {
      await load(body);

      expect(
        Array.from(root().querySelectorAll('.today__metric'), (m) =>
          m.textContent?.replace(/\s+/g, ''),
        ),
      ).toEqual(metricTexts.map((t) => t.replace(/\s+/g, '')));
      expect((text('.today__progress') ?? '').replace(/\s+/g, '')).toBe(
        progress.replace(/\s+/g, ''),
      );
      const bar = root().querySelector('[role="progressbar"]');
      expect(bar === null).toBe(progress === '');
      expect(root().querySelector('.today__next') === null).toBe(nextOrEmpty.includes('.'));
      expect(text('.today__next-label') ?? text('.today__empty')).toBe(nextOrEmpty);
      expect(root().querySelector('.today__route') === null).toBe(body.visits.length === 0);
    },
  );

  it('renders the next card, Call and Directions variants, the ordered route and read-only navigation (AC-09, AC-10, AC-12, AC-13)', async () => {
    vi.useFakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval'] });
    vi.setSystemTime(new Date('2026-10-06T19:00:00Z')); // 08:00 in Auckland
    await load(
      response(
        [
          visit('1', 'completed'),
          visit('2', 'scheduled'),
          visit('3', 'on_the_way'),
          visit('4', 'paused'),
          visit('5', 'needs_correction'),
        ],
        { scheduledMinutes: 480 },
        '2',
      ),
    );

    expect(text('.today__greeting')).toBe('Good morning, Carlos');
    expect(text('.today__date')).toBe('Wednesday, Oct 7, 2026');
    const card = root().querySelector('.today__next')!;
    expect(card.textContent).toContain('High priority');
    expect(card.textContent).toContain('#WO-2');
    expect(card.textContent).toContain('1842 Oak Street, Austin, TX 78704');
    expect(text('.today__arrival')).toBe('9:30 AM – 10:30 AM Arrival window');
    expect(card.textContent).toContain('2 planned materials');
    expect(card.textContent).not.toMatch(/Ready|Message|map|min away/i);
    const call = card.querySelector('a[href^="tel:"]')!;
    expect(call.getAttribute('href')).toBe('tel:5125550100');
    const directions = card.querySelector<HTMLAnchorElement>('a[target="_blank"]')!;
    expect(directions.getAttribute('href')).toBe(
      'https://www.google.com/maps/dir/?api=1&destination=30.25,-97.75',
    );
    expect(directions.getAttribute('rel')).toContain('noopener');
    expect(directions.getAttribute('aria-label')).toContain('opens in a new tab');
    const actions = Array.from(card.querySelectorAll('a'), (a) => [
      a.textContent?.trim(),
      a.getAttribute('href'),
    ]).filter(([label]) => label === 'Start travel' || label === 'View job details');
    expect(actions).toEqual([['View job details', '/today/visits/2']]);
    expect(card.querySelector('button')).toBeNull(); // a `scheduled` next visit cannot start travel

    const rows = Array.from(root().querySelectorAll('.today__route-list > li > a'));
    expect(root().querySelector('ol.today__route-list')).not.toBeNull();
    expect(rows.map((r) => r.getAttribute('href'))).toEqual([
      '/today/visits/1',
      '/today/visits/2',
      '/today/visits/3',
      '/today/visits/4',
      '/today/visits/5',
    ]);
    expect(rows.map((r) => r.querySelector('.today__chip')?.textContent?.trim())).toEqual([
      'Completed',
      'Next',
      'On the way',
      'Paused',
      'Needs correction',
    ]);
    expect(rows[0].querySelector('.pi-check')).not.toBeNull();
    expect(rows[1].classList.contains('today__row--next')).toBe(true);
    expect(rows[1].textContent).toContain('9:30 AM');
    expect(root().querySelector('.today__route')?.textContent).not.toMatch(/map|drive/i);
  });

  it('hides optional card lines and encodes the address when data is missing; greets by local time (AC-09, AC-13)', async () => {
    vi.useFakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval'] });
    vi.setSystemTime(new Date('2026-10-07T06:00:00Z')); // 19:00 in Auckland
    await load(
      response(
        [
          visit('1', 'assigned', {
            priority: 3,
            arrivalWindowEnd: '2026-10-07T09:30:00+13:00',
            plannedMaterialsCount: 0,
            phone: null,
            latitude: null,
            longitude: null,
          }),
        ],
        { scheduledMinutes: 120 },
        '1',
      ),
    );

    const card = root().querySelector('.today__next')!;
    expect(text('.today__greeting')).toBe('Good evening, Carlos');
    expect(card.querySelector('p-tag')?.textContent).toContain('Plumbing');
    expect(card.querySelectorAll('p-tag')).toHaveLength(1);
    expect(card.textContent).toContain('Arrival at 9:30 AM');
    expect(card.textContent).not.toContain('planned material');
    expect(card.querySelector('a[href^="tel:"]')).toBeNull();
    expect(card.querySelector('a[target="_blank"]')?.getAttribute('href')).toBe(
      `https://www.google.com/maps/dir/?api=1&destination=${encodeURIComponent('1842 Oak Street, Austin, TX 78704')}`,
    );
  });

  it('refreshes once at a time, ages the updated text and keeps data with an inline error on failure (AC-14)', async () => {
    vi.useFakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval'] });
    vi.setSystemTime(new Date('2026-10-07T00:00:00Z'));
    const body = response([visit('1', 'scheduled')], { scheduledMinutes: 120 }, '1');
    await load(body);
    expect(text('.today__updated span')).toBe('Updated just now');

    vi.advanceTimersByTime(5 * 60_000);
    await settle();
    expect(text('.today__updated span')).toBe('Updated 5 min ago');

    refreshButton().click();
    refreshButton().click();
    await settle();
    expect(refreshButton().getAttribute('aria-busy')).toBe('true');
    httpTesting.expectOne(TODAY_URL).flush(body);
    await settle();
    expect(text('.today__updated span')).toBe('Updated just now');

    refreshButton().click();
    httpTesting.expectOne(TODAY_URL).flush(null, { status: 500, statusText: 'Server Error' });
    await settle();
    expect(root().querySelector('p-message')?.textContent).toContain(
      "We couldn't load today's jobs. Try again.",
    );
    expect(root().querySelector('.today__next')).not.toBeNull();
  });

  it.each<[string, TodayVisit, boolean]>([
    ['an assigned visit where the caller is primary', visit('2', 'assigned'), true],
    [
      'an assigned visit where the caller is not primary',
      visit('2', 'assigned', { isPrimary: false }),
      false,
    ],
    ['an on-the-way visit', visit('2', 'on_the_way'), false],
  ])(
    'offers Start travel only on %s; starting records the transition before opening the job, failing inline and staying (AC-16)',
    async (_name, next, startable) => {
      await load(response([next], { scheduledMinutes: 60 }, '2'));
      const card = root().querySelector('.today__next')!;
      const start = () =>
        Array.from(card.querySelectorAll('button')).find((b) =>
          b.textContent?.includes('Start travel'),
        );
      const detailLink = Array.from(card.querySelectorAll('a')).find((a) =>
        a.textContent?.includes('View job details'),
      );
      expect(detailLink?.getAttribute('href')).toBe('/today/visits/2');
      expect(start() !== undefined).toBe(startable);
      if (!startable) {
        return;
      }
      const router = TestBed.inject(Router);

      // 409 keeps the card with the fixed message and no navigation.
      start()!.click();
      start()!.click();
      await settle();
      expect(start()!.disabled).toBe(true);
      const failing = httpTesting.expectOne({
        method: 'POST',
        url: `${API}/technician/visits/2/start-travel`,
      });
      expect(failing.request.body).toBeNull();
      failing.flush(
        { status: 409, code: 'another_visit_active' },
        { status: 409, statusText: 'Conflict' },
      );
      await settle();
      expect(text('.today__start-error')).toBe(
        "You're already traveling to or working on another job.",
      );
      expect(router.url).not.toContain('/today/visits');
      expect(start()!.disabled).toBe(false);

      // Success opens the job page after the POST.
      start()!.click();
      await settle();
      expect(router.url).not.toContain('/today/visits');
      httpTesting
        .expectOne({ method: 'POST', url: `${API}/technician/visits/2/start-travel` })
        .flush({ changed: true, visit: {} });
      await settle();
      expect(router.url).toBe('/today/visits/2');
    },
  );

  it.each([
    [
      404,
      'technician_profile_not_linked',
      "Your team profile isn't linked yet. Contact your manager.",
    ],
    [403, 'technician_inactive', 'Your technician profile is inactive. Contact your manager.'],
  ])('shows the %i %s state without visit data (AC-03)', async (status, code, message) => {
    create();
    httpTesting.expectOne(TODAY_URL).flush({ status, code }, { status, statusText: 'Error' });
    await settle();

    expect(text('.today__notice')).toBe(message);
    expect(root().querySelector('.today__next, .today__route, .today__metrics')).toBeNull();
  });

  it('shows the error state with Retry that reloads, and the forbidden state without a request for other roles (AC-03, AC-15)', async () => {
    create();
    httpTesting.expectOne(TODAY_URL).flush(null, { status: 500, statusText: 'Server Error' });
    await settle();
    expect(root().querySelector('p-message')?.textContent).toContain(
      "We couldn't load today's jobs. Try again.",
    );

    root().querySelector<HTMLButtonElement>('p-message button')!.click();
    httpTesting.expectOne(TODAY_URL).flush(response([], {}, null));
    await settle();
    expect(text('.today__empty')).toBe(NO_JOBS_MESSAGE);

    TestBed.resetTestingModule();
    create('dispatcher');
    await settle();
    expect(text('.today__notice')).toBe("You don't have access to Today's jobs.");
    httpTesting.expectNone(TODAY_URL);
  });
});
